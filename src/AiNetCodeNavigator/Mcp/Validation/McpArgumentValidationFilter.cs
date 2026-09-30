using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using AiNetCodeNavigator.Mcp.Formatting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NJsonSchema;
using NJsonSchema.Validation;

namespace AiNetCodeNavigator.Mcp.Validation;

/// <summary>Validates incoming tool arguments against the schema advertised by the registered SDK tool.</summary>
internal static class McpArgumentValidationFilter
{
    private static readonly ConditionalWeakTable<McpServerTool, Lazy<Task<JsonSchema>>> Schemas = new();

    internal static void Configure(IMcpRequestFilterBuilder filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            var error = await ValidateAsync(context, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                return error;
            }

            return await next(context, cancellationToken).ConfigureAwait(false);
        });
    }

    internal static async Task<CallToolResult?> ValidateAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.MatchedPrimitive is not McpServerTool tool)
        {
            return null;
        }

        var inputSchema = tool.ProtocolTool.InputSchema;
        if (inputSchema.ValueKind != JsonValueKind.Object)
        {
            return SchemaUnavailable();
        }

        JsonSchema schema;
        try
        {
            if (HasExternalReference(inputSchema))
            {
                return SchemaUnavailable();
            }

            schema = await Schemas.GetValue(
                tool,
                static registeredTool => new Lazy<Task<JsonSchema>>(
                    () => ParseInputSchemaAsync(registeredTool.ProtocolTool.InputSchema),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A registered tool schema is trusted metadata. If it cannot be parsed, do not invoke the tool or
            // expose schema/parser details to the caller.
            return SchemaUnavailable();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var arguments = context.Params?.Arguments ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var validationErrors = schema.Validate(JsonSerializer.Serialize(arguments));
        if (validationErrors.Count > 0)
        {
            return CreateInvalidArgument(validationErrors.First(), inputSchema, arguments);
        }

        if (TryGetBindingMetadata(tool, out var method, out var serializerOptions))
        {
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(CancellationToken)
                    || parameter.Name is null
                    || !arguments.TryGetValue(parameter.Name, out var value))
                {
                    continue;
                }

                try
                {
                    _ = JsonSerializer.Deserialize(value.GetRawText(), parameter.ParameterType, serializerOptions);
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException or OverflowException)
                {
                    return CreateInvalidArgument(AppendPropertyPath("$", parameter.Name), "The value cannot be bound to the registered tool parameter.", arguments);
                }
            }
        }

        return null;
    }

    private static CallToolResult CreateInvalidArgument(
        ValidationError error,
        JsonElement inputSchema,
        IDictionary<string, JsonElement> arguments) =>
        CreateInvalidArgument(ResolveFieldPath(error, inputSchema, arguments),
            "The supplied arguments do not match the registered tool input schema.", arguments);

    private static CallToolResult CreateInvalidArgument(
        string fieldPath,
        string message,
        IDictionary<string, JsonElement> arguments)
    {
        var (maxResponseBytes, maxResponseTokens) = ReadErrorBudgets(arguments);
        try
        {
            return McpToolResults.InvalidArgument(
                message,
                fieldPath,
                "Correct the argument at fieldPath using the tool's advertised input schema, then repeat the call.",
                maxResponseBytes: maxResponseBytes,
                maxResponseTokens: maxResponseTokens);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new McpProtocolException(
                "The response token budget is too small to return the required argument error.",
                null,
                McpErrorCode.InvalidParams);
        }
    }

    private static string ResolveFieldPath(
        ValidationError error,
        JsonElement inputSchema,
        IDictionary<string, JsonElement> arguments)
    {
        var rawPath = string.IsNullOrWhiteSpace(error.Path) ? "$" : error.Path;
        var path = NormalizePath(rawPath);
        if (error.Kind == ValidationErrorKind.PropertyRequired && path == "$")
        {
            using var argumentsDocument = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            var locatedPath = FindMissingRequiredPath(
                inputSchema,
                inputSchema,
                argumentsDocument.RootElement,
                "$",
                error.Property,
                0);
            if (locatedPath is not null) return locatedPath;
        }

        if (error.Kind == ValidationErrorKind.PropertyRequired && !string.IsNullOrWhiteSpace(error.Property))
        {
            var propertySuffix = $".{error.Property}";
            if (!path.EndsWith(propertySuffix, StringComparison.Ordinal))
            {
                return AppendPropertyPath(path, error.Property);
            }
        }

        return IsSafePath(path) ? path : "$";
    }

    private static string? FindMissingRequiredPath(
        JsonElement rootSchema,
        JsonElement schema,
        JsonElement value,
        string valuePath,
        string? missingName,
        int depth)
    {
        if (depth > 64 || schema.ValueKind != JsonValueKind.Object) return null;

        if (schema.TryGetProperty("$ref", out var reference) && reference.ValueKind == JsonValueKind.String
            && TryResolveLocalReference(rootSchema, reference.GetString()!, out var referencedSchema))
        {
            var referencedPath = FindMissingRequiredPath(rootSchema, referencedSchema, value, valuePath, missingName, depth + 1);
            if (referencedPath is not null) return referencedPath;
        }

        if (value.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var requiredName in required.EnumerateArray())
            {
                if (requiredName.ValueKind != JsonValueKind.String) continue;
                var name = requiredName.GetString();
                if (name is not null
                    && string.Equals(name, missingName, StringComparison.Ordinal)
                    && !value.TryGetProperty(name, out _))
                {
                    return AppendPropertyPath(valuePath, name);
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertySchema in properties.EnumerateObject())
            {
                if (!value.TryGetProperty(propertySchema.Name, out var propertyValue)) continue;
                var nestedPath = AppendPropertyPath(valuePath, propertySchema.Name);
                var result = FindMissingRequiredPath(rootSchema, propertySchema.Value, propertyValue, nestedPath, missingName, depth + 1);
                if (result is not null) return result;
            }
        }

        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                var result = FindMissingRequiredPath(rootSchema, items, item, $"{valuePath}[{index}]", missingName, depth + 1);
                if (result is not null) return result;
                index++;
            }
        }

        foreach (var composition in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (!schema.TryGetProperty(composition, out var schemas) || schemas.ValueKind != JsonValueKind.Array) continue;
            foreach (var alternative in schemas.EnumerateArray())
            {
                var result = FindMissingRequiredPath(rootSchema, alternative, value, valuePath, missingName, depth + 1);
                if (result is not null) return result;
            }
        }

        return null;
    }

    private static bool TryResolveLocalReference(JsonElement rootSchema, string reference, out JsonElement resolved)
    {
        resolved = default;
        if (!reference.StartsWith("#", StringComparison.Ordinal)) return false;
        var current = rootSchema;
        foreach (var segment in reference.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var propertyName = segment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(propertyName, out current)) return false;
        }

        resolved = current;
        return true;
    }

    private static string NormalizePath(string path)
    {
        if (!path.StartsWith("#", StringComparison.Ordinal)) return path;

        var segments = path.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = "$";
        foreach (var rawSegment in segments)
        {
            var segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            if (bracket > 0 && segment.EndsWith(']')
                && segment[..bracket].All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
                && int.TryParse(segment[(bracket + 1)..^1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var arrayIndex))
            {
                normalized += $".{segment[..bracket]}[{arrayIndex}]";
                continue;
            }

            if (int.TryParse(segment, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var index))
            {
                normalized += $"[{index}]";
            }
            else if (segment.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            {
                normalized += $".{segment}";
            }
            else
            {
                return "$";
            }
        }

        return normalized;
    }

    private static string AppendPropertyPath(string path, string propertyName) =>
        propertyName.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            ? $"{path}.{propertyName}"
            : "$";

    private static bool IsSafePath(string path) => path.StartsWith('$')
        && path.All(static character => char.IsAsciiLetterOrDigit(character) || character is '$' or '.' or '_' or '-' or '[' or ']');

    private static (int MaxResponseBytes, int? MaxResponseTokens) ReadErrorBudgets(
        IDictionary<string, JsonElement> arguments)
    {
        var bytes = TryReadInt32(arguments, "maxResponseBytes", out var configuredBytes)
            && McpResponseBudgetLimits.IsPublicBudget(configuredBytes)
                ? configuredBytes
                : McpResponseBudgetLimits.DefaultBytes;
        int? tokens = TryReadInt32(arguments, "maxResponseTokens", out var configuredTokens) && configuredTokens > 0
            ? configuredTokens
            : null;
        return (bytes, tokens);
    }

    private static bool TryReadInt32(IDictionary<string, JsonElement> arguments, string name, out int value)
    {
        value = default;
        return arguments.TryGetValue(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool HasExternalReference(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in schema.EnumerateObject())
            {
                if (property.NameEquals("$ref")
                    && (property.Value.ValueKind != JsonValueKind.String
                        || !property.Value.GetString()!.StartsWith('#')))
                {
                    return true;
                }

                if (HasExternalReference(property.Value)) return true;
            }
        }
        else if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
            {
                if (HasExternalReference(item)) return true;
            }
        }

        return false;
    }


    private static CallToolResult SchemaUnavailable() => McpToolResults.Recoverable(
        "TOOL_SCHEMA_UNAVAILABLE",
        "The registered tool input schema could not be validated.",
        "Retry the call later or use another available tool.");

    internal static async Task<JsonSchema> ParseInputSchemaAsync(JsonElement inputSchema)
    {
        if (HasExternalReference(inputSchema))
        {
            throw new NotSupportedException("External schema references are not allowed for registered tool inputs.");
        }

        var parsed = await JsonSchema.FromJsonAsync(inputSchema.GetRawText()).ConfigureAwait(false);
        // MCP tool inputs are named argument objects. Close that top-level object even when the SDK's generated
        // schema omits additionalProperties; nested objects follow their declared schema, including explicit maps.
        if (parsed.Type.HasFlag(JsonObjectType.Object) || parsed.Properties.Count > 0)
        {
            parsed.AllowAdditionalProperties = false;
        }

        return parsed;
    }

    private static bool TryGetBindingMetadata(
        McpServerTool tool,
        out MethodInfo method,
        out JsonSerializerOptions? serializerOptions)
    {
        // The SDK's concrete AIFunction-backed tool type is intentionally internal. Read its public AIFunction
        // metadata without depending on that concrete implementation type so primitive binding failures are
        // caught before dispatch, including integer formats omitted from MCP's generated schema.
        method = null!;
        serializerOptions = null;
        var functionProperty = tool.GetType().GetProperty("AIFunction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var function = functionProperty?.GetValue(tool);
        if (function is null) return false;

        method = function.GetType().GetProperty("UnderlyingMethod", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(function) as MethodInfo
            ?? null!;
        if (method is null) return false;

        serializerOptions = function.GetType().GetProperty("JsonSerializerOptions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(function) as JsonSerializerOptions;
        return true;
    }
}

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

        var arguments = context.Params?.Arguments ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var inputSchema = tool.ProtocolTool.InputSchema;
        if (inputSchema.ValueKind != JsonValueKind.Object)
        {
            return SchemaUnavailable(arguments);
        }

        JsonSchema schema;
        try
        {
            if (HasExternalReference(inputSchema))
            {
                return SchemaUnavailable(arguments);
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
            return SchemaUnavailable(arguments);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var unknownArgument = arguments.Keys.FirstOrDefault(key => !HasRootProperty(inputSchema, inputSchema, key, new HashSet<string>(StringComparer.Ordinal), 0));
        if (unknownArgument is not null)
        {
            return CreateInvalidArgument(AppendPropertyPath("$", unknownArgument), "The supplied argument is not declared by the registered tool input schema.", arguments);
        }

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
                    || !arguments.TryGetValue(GetWireParameterName(parameter), out var value))
                {
                    continue;
                }

                try
                {
                    _ = JsonSerializer.Deserialize(value.GetRawText(), parameter.ParameterType, serializerOptions);
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException or OverflowException)
                {
                    return CreateInvalidArgument(AppendPropertyPath("$", GetWireParameterName(parameter)), "The value cannot be bound to the registered tool parameter.", arguments);
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
        if (error is ChildSchemaValidationError childSchemaError)
        {
            foreach (var nestedError in childSchemaError.Errors.Values.SelectMany(static errors => errors))
            {
                var nestedPath = ResolveFieldPath(nestedError, inputSchema, arguments);
                if (nestedPath != "$") return nestedPath;
            }
        }

        var rawPath = string.IsNullOrWhiteSpace(error.Path) ? "$" : error.Path;
        var path = NormalizePath(rawPath);
        if (error.Kind == ValidationErrorKind.PropertyRequired)
        {
            using var argumentsDocument = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            var locatedPath = FindMissingRequiredPath(
                inputSchema,
                inputSchema,
                argumentsDocument.RootElement,
                "$",
                error.Property,
                0);
            if (locatedPath is not null) return locatedPath.Length == 0 ? "$" : locatedPath;

            if (path != "$")
            {
                if (TryGetExistingValue(argumentsDocument.RootElement, path, out _))
                {
                    return string.IsNullOrWhiteSpace(error.Property) ? path : AppendPropertyPath(path, error.Property);
                }

                return path;
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
                    return valuePath.Length > 0 && IsSafePathSegment(name)
                        ? AppendPropertyPath(valuePath, name)
                        : string.Empty;
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
                var nestedPath = valuePath.Length > 0 && IsSafePathSegment(propertySchema.Name)
                    ? AppendPropertyPath(valuePath, propertySchema.Name)
                    : string.Empty;
                var result = FindMissingRequiredPath(rootSchema, propertySchema.Value, propertyValue, nestedPath, missingName, depth + 1);
                if (result is not null) return result;
            }

            if (schema.TryGetProperty("additionalProperties", out var additionalProperties)
                && additionalProperties.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyValue in value.EnumerateObject())
                {
                    if (properties.TryGetProperty(propertyValue.Name, out _)) continue;
                    var nestedPath = valuePath.Length > 0 && IsSafePathSegment(propertyValue.Name)
                        ? AppendPropertyPath(valuePath, propertyValue.Name)
                        : string.Empty;
                    var result = FindMissingRequiredPath(rootSchema, additionalProperties, propertyValue.Value, nestedPath, missingName, depth + 1);
                    if (result is not null) return result;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("additionalProperties", out var onlyAdditionalProperties)
            && onlyAdditionalProperties.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyValue in value.EnumerateObject())
            {
                var nestedPath = valuePath.Length > 0 && IsSafePathSegment(propertyValue.Name)
                    ? AppendPropertyPath(valuePath, propertyValue.Name)
                    : string.Empty;
                var result = FindMissingRequiredPath(rootSchema, onlyAdditionalProperties, propertyValue.Value, nestedPath, missingName, depth + 1);
                if (result is not null) return result;
            }
        }

        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                var itemPath = valuePath.Length > 0 ? $"{valuePath}[{index}]" : string.Empty;
                var result = FindMissingRequiredPath(rootSchema, items, item, itemPath, missingName, depth + 1);
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
            foreach (var pathPart in segment.Split('.', StringSplitOptions.None))
            {
                if (pathPart.Length == 0) return "$";
                var bracket = pathPart.IndexOf('[', StringComparison.Ordinal);
                if (bracket > 0 && pathPart.EndsWith(']')
                    && IsSafePathSegment(pathPart[..bracket])
                    && int.TryParse(pathPart[(bracket + 1)..^1], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var arrayIndex))
                {
                    normalized += $".{pathPart[..bracket]}[{arrayIndex}]";
                    continue;
                }

                if (int.TryParse(pathPart, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var index))
                {
                    normalized += $"[{index}]";
                }
                else if (IsSafePathSegment(pathPart))
                {
                    normalized += $".{pathPart}";
                }
                else
                {
                    return "$";
                }
            }
        }

        return normalized;
    }

    private static string AppendPropertyPath(string path, string propertyName) =>
        IsSafePathSegment(propertyName)
            ? $"{path}.{propertyName}"
            : "$";

    private static bool IsSafePathSegment(string value) => value.Length > 0
        && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool TryGetExistingValue(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        if (path == "$") return true;
        if (!path.StartsWith("$.", StringComparison.Ordinal)) return false;

        var index = 2;
        while (index < path.Length)
        {
            var segmentStart = index;
            while (index < path.Length && path[index] is not '.' and not '[') index++;
            if (segmentStart < index)
            {
                if (value.ValueKind != JsonValueKind.Object
                    || !value.TryGetProperty(path[segmentStart..index], out var propertyValue)) return false;
                value = propertyValue;
            }

            if (index < path.Length && path[index] == '[')
            {
                var close = path.IndexOf(']', index + 1);
                if (close < 0 || !int.TryParse(path[(index + 1)..close], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var arrayIndex)
                    || value.ValueKind != JsonValueKind.Array || arrayIndex < 0 || arrayIndex >= value.GetArrayLength())
                {
                    return false;
                }

                value = value[arrayIndex];
                index = close + 1;
            }

            if (index < path.Length && path[index] == '.') index++;
        }

        return true;
    }

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


    private static CallToolResult SchemaUnavailable(IDictionary<string, JsonElement> arguments)
    {
        var (maxResponseBytes, maxResponseTokens) = ReadErrorBudgets(arguments);
        try
        {
            return McpToolResults.Recoverable(
                "TOOL_SCHEMA_UNAVAILABLE",
                "The registered tool input schema could not be validated.",
                "Retry the call later or use another available tool.",
                maxResponseBytes: maxResponseBytes,
                maxResponseTokens: maxResponseTokens);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new McpProtocolException(
                "The response token budget is too small to return the required schema error.",
                null,
                McpErrorCode.InvalidParams);
        }
    }

    internal static async Task<JsonSchema> ParseInputSchemaAsync(JsonElement inputSchema)
    {
        if (HasExternalReference(inputSchema))
        {
            throw new NotSupportedException("External schema references are not allowed for registered tool inputs.");
        }

        return await JsonSchema.FromJsonAsync(inputSchema.GetRawText()).ConfigureAwait(false);
    }

    private static bool HasRootProperty(
        JsonElement rootSchema,
        JsonElement schema,
        string name,
        HashSet<string> visitedReferences,
        int depth)
    {
        if (depth > 64 || schema.ValueKind != JsonValueKind.Object) return false;

        if (schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty(name, out _))
        {
            return true;
        }

        if (schema.TryGetProperty("$ref", out var reference)
            && reference.ValueKind == JsonValueKind.String
            && visitedReferences.Add(reference.GetString()!)
            && TryResolveLocalReference(rootSchema, reference.GetString()!, out var referencedSchema)
            && HasRootProperty(rootSchema, referencedSchema, name, visitedReferences, depth + 1))
        {
            return true;
        }

        foreach (var composition in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (!schema.TryGetProperty(composition, out var schemas) || schemas.ValueKind != JsonValueKind.Array) continue;
            foreach (var alternative in schemas.EnumerateArray())
            {
                if (HasRootProperty(rootSchema, alternative, name, visitedReferences, depth + 1)) return true;
            }
        }

        return false;
    }

    private static string GetWireParameterName(ParameterInfo parameter)
    {
        const string parameterNameAttribute = "Microsoft.Extensions.AI.AIParameterNameAttribute";
        var renamedAttribute = parameter.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType.FullName == parameterNameAttribute);
        if (renamedAttribute?.ConstructorArguments is [{ Value: string explicitName }])
        {
            return explicitName;
        }

        // MCP binds method parameters by their CLR parameter name unless AIParameterNameAttribute explicitly
        // changes the function parameter name. JsonSerializerOptions.PropertyNamingPolicy applies to object
        // members; it does not rename the SDK's method-parameter keys.
        return parameter.Name!;
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

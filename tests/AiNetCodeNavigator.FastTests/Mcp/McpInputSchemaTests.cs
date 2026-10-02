using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AiNetCodeNavigator.Mcp.Validation;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NJsonSchema;

namespace AiNetCodeNavigator.FastTests.Mcp;

[Trait("Category", "Unit")]
public sealed class McpInputSchemaTests
{
    [Fact]
    public async Task ResolvesLocalDefinitionsAndValidatesExplicitAdditionalPropertiesSchema()
    {
        using var schemaDocument = JsonDocument.Parse("""
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": { "options": { "$ref": "#/$defs/options" } },
              "required": ["options"],
              "$defs": {
                "options": {
                  "type": "object",
                  "properties": { "label": { "type": "string" } },
                  "required": ["label"],
                  "additionalProperties": { "type": "integer" }
                }
              }
            }
            """);

        var schema = await McpArgumentValidationFilter.ParseInputSchemaAsync(schemaDocument.RootElement);

        Assert.Empty(schema.Validate("{\"options\":{\"label\":\"source\",\"extra\":2}}"));
        Assert.NotEmpty(schema.Validate("{\"options\":{\"label\":\"source\",\"extra\":\"wrong\"}}"));
    }

    [Fact]
    public async Task RejectsExternalReferencesWithoutFetchingThem()
    {
        using var schemaDocument = JsonDocument.Parse("{\"$ref\":\"https://example.invalid/schema.json\"}");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await McpArgumentValidationFilter.ParseInputSchemaAsync(schemaDocument.RootElement));
    }

    [Fact]
    public async Task OriginalSdkToolValidatorRejectsUnknownFieldsAcrossReferencedAndComposedRoots()
    {
        var tool = McpServerTool.Create((Func<int, string, string>)ValidationFixture.Accept,
            new McpServerToolCreateOptions { Name = "validator_schema_fixture" });
        using var referencedSchema = JsonDocument.Parse("""
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {},
              "$ref": "#/$defs/input",
              "$defs": {
                "input": {
                  "type": "object",
                  "properties": { "count": { "type": "integer" }, "label": { "type": "string" } },
                  "required": ["count", "label"]
                }
              }
            }
            """);
        tool.ProtocolTool.InputSchema = referencedSchema.RootElement.Clone();

        Assert.Null(await ValidateAsync(tool, """{"count":1,"label":"ok"}"""));
        var referencedUnknown = await ValidateAsync(tool, """{"count":1,"label":"ok","unexpected":true}""");
        AssertInvalidField(referencedUnknown, "$.unexpected");
        var unsafeUnknown = await ValidateAsync(tool, """{"count":1,"label":"ok","__proto__":true}""");
        AssertInvalidField(unsafeUnknown, "$");

        using var composedSchema = JsonDocument.Parse("""
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {},
              "allOf": [
                { "$ref": "#/$defs/count" },
                { "type": "object", "properties": { "label": { "type": "string" } } }
              ],
              "$defs": {
                "count": {
                  "type": "object",
                  "properties": { "count": { "type": "integer" } },
                  "required": ["count"]
                }
              },
              "required": ["label"]
            }
            """);
        tool.ProtocolTool.InputSchema = composedSchema.RootElement.Clone();
        Assert.Null(await ValidateAsync(tool, """{"count":1,"label":"ok"}"""));
        var composedUnknown = await ValidateAsync(tool, """{"count":1,"label":"ok","unexpected":true}""");
        AssertInvalidField(composedUnknown, "$.unexpected");
    }

    [Fact]
    public async Task OriginalSdkValidatorAndBinderCoverWireNamesTypesAndRejectedCallFollowup()
    {
        BinderFixture.Reset();
        var tool = McpServerTool.Create((Func<int, string[], BinderFixture.Mode, int?, string>)BinderFixture.Accept,
            new McpServerToolCreateOptions
            {
                Name = "sdk_binder_fixture",
                SerializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                },
            });

        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        Assert.Contains("sequence_value", properties.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("sequence", properties.EnumerateObject().Select(property => property.Name));
        Assert.True(properties.TryGetProperty("mode", out var modeSchema), tool.ProtocolTool.InputSchema.GetRawText());
        Assert.Equal("integer", modeSchema.GetProperty("type").GetString());
        Assert.Equal(new[] { "sequence_value", "names", "mode" },
            tool.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());

        foreach (var invalidJson in new[]
        {
            """{"names":["x"],"mode":0}""",
            """{"sequence_value":2147483648,"names":["x"],"mode":0,"maxResponseTokens":null}""",
            """{"sequence_value":1,"names":[1],"mode":0,"maxResponseTokens":null}""",
            """{"sequence_value":1,"names":["x"],"mode":"Strict","maxResponseTokens":null}""",
            """{"sequence_value":1,"names":["x"],"mode":0,"maxResponseTokens":0}""",
        })
        {
            var invalid = await ValidateAsync(tool, invalidJson);
            Assert.NotNull(invalid);
            Assert.True(invalid!.IsError);
            Assert.Equal(0, BinderFixture.InvocationCount);
        }

        const string validJson = """{"sequence_value":2147483647,"names":["x","y"],"mode":0,"maxResponseTokens":null}""";
        Assert.Null(await ValidateAsync(tool, validJson));
        using var validDocument = JsonDocument.Parse(validJson);
        var functionProperty = tool.GetType().GetProperty("AIFunction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var function = Assert.IsAssignableFrom<AIFunction>(functionProperty?.GetValue(tool));
        var boundResult = await function.InvokeAsync(new AIFunctionArguments
        {
            ["sequence_value"] = int.MaxValue,
            ["names"] = new[] { "x", "y" },
            ["mode"] = 0,
            ["maxResponseTokens"] = null,
        }, CancellationToken.None);
        Assert.Equal("2147483647:2:Strict:null", boundResult);
        Assert.Equal(1, BinderFixture.InvocationCount);
    }

    private static async Task<CallToolResult?> ValidateAsync(McpServerTool tool, string json)
    {
        using var document = JsonDocument.Parse(json);
        var arguments = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        return await McpArgumentValidationFilter.ValidateArgumentsAsync(tool, arguments);
    }

    private static void AssertInvalidField(CallToolResult? result, string fieldPath)
    {
        Assert.NotNull(result);
        Assert.True(result!.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains($"fieldPath: {fieldPath}", text, StringComparison.Ordinal);
    }

    private static class ValidationFixture
    {
        internal static string Accept(int count, string label) => $"{count}:{label}";
    }

    private static class BinderFixture
    {
        private static int _invocationCount;

        internal enum Mode
        {
            Strict,
            Loose,
        }

        internal static int InvocationCount => Volatile.Read(ref _invocationCount);

        internal static void Reset() => Interlocked.Exchange(ref _invocationCount, 0);

#pragma warning disable MEAI001 // Exercise the SDK's published wire name independently of serializer naming policy.
        internal static string Accept(
            [System.ComponentModel.DataAnnotations.Required, AIParameterName("sequence_value")] int sequence,
            [System.ComponentModel.DataAnnotations.MinLength(1)] string[] names,
            Mode mode,
            [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int? maxResponseTokens = null)
#pragma warning restore MEAI001
        {
            Interlocked.Increment(ref _invocationCount);
            return $"{sequence}:{names.Length}:{mode}:{maxResponseTokens?.ToString() ?? "null"}";
        }
    }
}

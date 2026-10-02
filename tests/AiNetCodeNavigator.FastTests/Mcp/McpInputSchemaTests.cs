using System.Text.Json;
using AiNetCodeNavigator.Mcp.Validation;
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
}

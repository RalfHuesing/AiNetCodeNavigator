using System.IO.Pipelines;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Mcp.Formatting;
using AiNetCodeNavigator.Mcp.Validation;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.AI;

namespace AiNetCodeNavigator.FastTests.Mcp;

public sealed class McpArgumentValidationFilterTests
{
    [Fact]
    public async Task RegisteredSdkTool_RejectsMissingNullWrongTypeUnknownAndInvalidArrayBeforeDispatch()
    {
        ArgumentValidationFixtureTool.Reset();

        await using var session = await FixtureSession.StartAsync();

        var missing = await session.CallAsync(new Dictionary<string, object?>
        {
            ["names"] = new[] { "alpha" },
            ["mode"] = "Exact",
        });
        AssertInvalidArgument(missing, "$.count");

        var nullValue = await session.CallAsync(new Dictionary<string, object?>
        {
            ["count"] = null,
            ["names"] = new[] { "alpha" },
            ["mode"] = "Exact",
        });
        AssertInvalidArgument(nullValue, "$.count");

        var wrongType = await session.CallAsync(new Dictionary<string, object?>
        {
            ["count"] = "one",
            ["names"] = new[] { "alpha" },
            ["mode"] = "Exact",
        });
        AssertInvalidArgument(wrongType, "$.count");

        var unknown = await session.CallAsync(new Dictionary<string, object?>
        {
            ["count"] = 1,
            ["names"] = new[] { "alpha" },
            ["mode"] = "Exact",
            ["unexpected"] = true,
        });
        AssertInvalidArgument(unknown, "$.unexpected");

        var invalidArrayElement = await session.CallAsync(new Dictionary<string, object?>
        {
            ["count"] = 1,
            ["names"] = new object?[] { "alpha", 2 },
            ["mode"] = "Exact",
        });
        AssertInvalidArgument(invalidArrayElement, "$.names[1]");
        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);
    }

    [Fact]
    public async Task RegisteredSdkTool_RejectsEnumCaseFractionalIntegerAndOverflowBeforeDispatch()
    {
        ArgumentValidationFixtureTool.Reset();
        await using var session = await FixtureSession.StartAsync();

        var badEnum = ValidArguments();
        badEnum["mode"] = "exact";
        var fractionalInteger = ValidArguments();
        fractionalInteger["sequence"] = 1.5;
        var integralDecimal = ValidArguments();
        integralDecimal["sequence"] = JsonDocument.Parse("3.0").RootElement.Clone();
        var exponentInteger = ValidArguments();
        exponentInteger["sequence"] = JsonDocument.Parse("1e3").RootElement.Clone();
        var overflowInteger = ValidArguments();
        overflowInteger["sequence"] = long.MaxValue;

        foreach (var arguments in new[] { badEnum, fractionalInteger, integralDecimal, exponentInteger, overflowInteger })
        {
            var result = await session.CallAsync(arguments);
            Assert.True(result.IsError);
            Assert.Contains("INVALID_ARGUMENT", TextOf(result), StringComparison.Ordinal);
            Assert.True(TextOf(result).Contains("fieldPath: $.", StringComparison.Ordinal), TextOf(result));
            Assert.Contains("nextAction:", TextOf(result), StringComparison.Ordinal);
        }

        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);
    }

    [Fact]
    public async Task RegisteredSdkTool_DispatchesSchemaValidArgumentsAndPropagatesCancellationToken()
    {
        ArgumentValidationFixtureTool.Reset();
        await using var session = await FixtureSession.StartAsync();

        var result = await session.CallAsync(ValidArguments());

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, ArgumentValidationFixtureTool.InvocationCount);
        Assert.True(ArgumentValidationFixtureTool.ReceivedToken.CanBeCanceled);
    }

    [Fact]
    public async Task RegisteredSdkTool_EnforcesAdvertisedRangesAndHonorsNullableInputs()
    {
        ArgumentValidationFixtureTool.Reset();
        await using var session = await FixtureSession.StartAsync();

        var valid = ValidArguments();
        valid["note"] = null;
        var validResult = await session.CallAsync(valid);
        Assert.NotEqual(true, validResult.IsError);

        var nullableOptions = ValidArguments();
        nullableOptions["options"] = null;
        Assert.NotEqual(true, (await session.CallAsync(nullableOptions)).IsError);

        var tooSmall = ValidArguments();
        tooSmall["count"] = 0;
        AssertInvalidArgument(await session.CallAsync(tooSmall), "$.count");

        var tooManyNames = ValidArguments();
        tooManyNames["names"] = new[] { "one", "two", "three" };
        AssertInvalidArgument(await session.CallAsync(tooManyNames), "$.names");

        var missingNested = ValidArguments();
        missingNested["options"] = new { };
        AssertInvalidArgument(await session.CallAsync(missingNested), "$.options.label");

        Assert.Equal(2, ArgumentValidationFixtureTool.InvocationCount);
    }

    [Fact]
    public async Task RegisteredSdkTool_UnrepresentableErrorTokenBudgetReturnsSanitizedProtocolError()
    {
        ArgumentValidationFixtureTool.Reset();
        await using var session = await FixtureSession.StartAsync();
        var arguments = ValidArguments();
        arguments.Remove("count");
        arguments["maxResponseBytes"] = 512;
        var boundedResult = await session.CallAsync(arguments);
        AssertInvalidArgument(boundedResult, "$.count");
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(TextOf(boundedResult)) <= 512);

        arguments["maxResponseTokens"] = 1;

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await session.CallAsync(arguments));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("token budget is too small", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ArgumentOutOfRangeException", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);
    }

    [Fact]
    public async Task RegisteredSdkTool_CancellationDoesNotDispatchTheHandler()
    {
        ArgumentValidationFixtureTool.Reset();
        await using var session = await FixtureSession.StartAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await session.CallAsync(ValidArguments(), cancellation.Token));

        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);
    }

    [Fact]
    public async Task RegisteredSdkTool_SchemaDescribesNullableInputs()
    {
        await using var session = await FixtureSession.StartAsync();

        var schemaDescription = await session.GetToolsJsonAsync();

        Assert.Contains("note", schemaDescription, StringComparison.Ordinal);
        Assert.Contains("options", schemaDescription, StringComparison.Ordinal);
        Assert.Contains("null", schemaDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InputSchema_ResolvesLocalDefsAndValidatesExplicitAdditionalPropertiesSchema()
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
    public async Task InputSchema_RejectsExternalReferencesWithoutFetchingThem()
    {
        using var schemaDocument = JsonDocument.Parse("{\"$ref\":\"https://example.invalid/schema.json\"}");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await McpArgumentValidationFilter.ParseInputSchemaAsync(schemaDocument.RootElement));
    }

    [Fact]
    public async Task RegisteredSdkTool_ClosesRootPropertiesThroughLocalReference()
    {
        ArgumentValidationFixtureTool.Reset();
        var tool = CreateTool(
            (Func<int, string>)ArgumentValidationFixtureTool.Referenced,
            """
            {"type":"object","$ref":"#/$defs/input","$defs":{"input":{"type":"object","properties":{"count":{"type":"integer"}},"required":["count"]}}}
            """);
        await using var session = await FixtureSession.StartAsync(tool);

        var invalid = await session.CallAsync(new Dictionary<string, object?> { ["count"] = 1, ["unknown"] = true });
        AssertInvalidArgument(invalid, "$.unknown");
        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);

        var valid = await session.CallAsync(new Dictionary<string, object?> { ["count"] = 1 });
        Assert.NotEqual(true, valid.IsError);
        Assert.Equal(1, ArgumentValidationFixtureTool.InvocationCount);
    }

    [Fact]
    public async Task RegisteredSdkTool_ClosesComposedRootWithoutLosingDeclaredProperties()
    {
        ArgumentValidationFixtureTool.Reset();
        var tool = CreateTool(
            (Func<int, string, string>)ArgumentValidationFixtureTool.RootProperties,
            """
            {"type":"object","allOf":[{"$ref":"#/$defs/input"},{"type":"object","properties":{"label":{"type":"string"}},"required":["label"]}],"$defs":{"input":{"type":"object","properties":{"count":{"type":"integer"}},"required":["count"]}}}
            """);
        await using var session = await FixtureSession.StartAsync(tool);

        var invalid = await session.CallAsync(new Dictionary<string, object?> { ["count"] = 1, ["label"] = "ok", ["unknown"] = true });
        AssertInvalidArgument(invalid, "$.unknown");
        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);

        var valid = await session.CallAsync(new Dictionary<string, object?> { ["count"] = 1, ["label"] = "ok" });
        Assert.True(valid.IsError is not true, TextOf(valid));
        Assert.Equal(1, ArgumentValidationFixtureTool.InvocationCount);
    }

    [Fact]
    public async Task RegisteredSdkTool_UsesAdvertisedParameterNameForBindingChecks()
    {
        ArgumentValidationFixtureTool.Reset();
        await using var session = await FixtureSession.StartAsync(
            CreateTool((Func<int, string>)ArgumentValidationFixtureTool.Renamed));

        var invalid = await session.CallAsync(new Dictionary<string, object?> { ["sequence_value"] = 2_147_483_648L });

        AssertInvalidArgument(invalid, "$.sequence_value");
        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);

        var valid = await session.CallAsync(new Dictionary<string, object?> { ["sequence_value"] = 2_147_483_647 });
        Assert.NotEqual(true, valid.IsError);
        Assert.Equal(1, ArgumentValidationFixtureTool.InvocationCount);
    }

    [Fact]
    public async Task RegisteredSdkTool_AppliesTokenBudgetToSchemaUnavailableErrors()
    {
        ArgumentValidationFixtureTool.Reset();
        var tool = CreateTool(
            (Func<int, string>)ArgumentValidationFixtureTool.Referenced,
            "{\"type\":\"object\",\"$ref\":\"https://example.invalid/schema.json\"}");
        await using var session = await FixtureSession.StartAsync(tool);

        var defaultEnvelope = McpToolResults.Recoverable(
            "TOOL_SCHEMA_UNAVAILABLE",
            "The registered tool input schema could not be validated.",
            "Retry the call later or use another available tool.",
            maxResponseBytes: 512);
        var requiredTokens = McpResponseFormatter.CountTokens(TextOf(defaultEnvelope));
        var exactBudget = await session.CallAsync(new Dictionary<string, object?>
        {
            ["count"] = 1,
            ["maxResponseBytes"] = 512,
            ["maxResponseTokens"] = requiredTokens,
        });
        Assert.True(exactBudget.IsError);
        Assert.True(McpResponseFormatter.CountTokens(TextOf(exactBudget)) <= requiredTokens);

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await session.CallAsync(new Dictionary<string, object?>
            {
                ["count"] = 1,
                ["maxResponseBytes"] = 512,
                ["maxResponseTokens"] = requiredTokens - 1,
            }));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("token budget is too small", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.invalid", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);
    }

    [Fact]
    public async Task RegisteredSdkTool_ReportsRequiredPathsInsideDictionaryAndArrayValues()
    {
        ArgumentValidationFixtureTool.Reset();
        var objectSchema = """
            {
              "type":"object",
              "properties":{"options":{"type":"object","additionalProperties":{"$ref":"#/$defs/item"}}},
              "required":["options"],
              "$defs":{"item":{"type":"object","properties":{"label":{"type":"string"}},"required":["label"]}}
            }
            """;
        var objectTool = CreateTool(
            (Func<Dictionary<string, ArgumentValidationFixtureTool.FixtureOptions>, string>)ArgumentValidationFixtureTool.DictionaryObject,
            objectSchema);
        await using (var objectSession = await FixtureSession.StartAsync(objectTool))
        {
            var missingDictionaryField = await objectSession.CallAsync(new Dictionary<string, object?>
            {
                ["options"] = new Dictionary<string, object?> { ["first"] = new { } },
            });
            AssertInvalidArgument(missingDictionaryField, "$.options.first.label");
            Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);
        }

        var schema = """
            {
              "type":"object",
              "properties":{"options":{"type":"object","additionalProperties":{"type":"array","items":{"$ref":"#/$defs/item"}}}},
              "required":["options"],
              "$defs":{"item":{"type":"object","properties":{"label":{"type":"string"}},"required":["label"]}}
            }
            """;
        var tool = CreateTool(
            (Func<Dictionary<string, List<ArgumentValidationFixtureTool.FixtureOptions>>, string>)ArgumentValidationFixtureTool.DictionaryArray,
            schema);
        await using var session = await FixtureSession.StartAsync(tool);

        var ordinaryKey = await session.CallAsync(new Dictionary<string, object?>
        {
            ["options"] = new Dictionary<string, object?> { ["first"] = new object?[] { new { } } },
        });
        AssertInvalidArgument(ordinaryKey, "$.options.first[0].label");
        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);

        var unsafeKey = await session.CallAsync(new Dictionary<string, object?>
        {
            ["options"] = new Dictionary<string, object?> { ["raw/key"] = new object?[] { new { } } },
        });
        Assert.True(unsafeKey.IsError);
        Assert.Contains("fieldPath: $", TextOf(unsafeKey), StringComparison.Ordinal);
        Assert.DoesNotContain("raw/key", TextOf(unsafeKey), StringComparison.Ordinal);
        Assert.Equal(0, ArgumentValidationFixtureTool.InvocationCount);
    }

    private static Dictionary<string, object?> ValidArguments() => new()
    {
        ["count"] = 2,
        ["names"] = new[] { "alpha", "beta" },
        ["mode"] = "Exact",
        ["sequence"] = 2,
        ["note"] = null,
        ["options"] = new ArgumentValidationFixtureTool.FixtureOptions { Label = "source" },
    };

    private static void AssertInvalidArgument(CallToolResult result, string fieldPath)
    {
        Assert.True(result.IsError);
        Assert.Contains("INVALID_ARGUMENT", TextOf(result), StringComparison.Ordinal);
        Assert.True(TextOf(result).Contains($"fieldPath: {fieldPath}", StringComparison.Ordinal), TextOf(result));
        Assert.Contains("nextAction:", TextOf(result), StringComparison.Ordinal);
    }

    private static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static McpServerTool CreateTool(Delegate handler, string? inputSchema = null)
    {
        var tool = McpServerTool.Create(handler, new McpServerToolCreateOptions { Name = "argument_validation_fixture" });
        if (inputSchema is not null)
        {
            using var schema = JsonDocument.Parse(inputSchema);
            tool.ProtocolTool.InputSchema = schema.RootElement.Clone();
        }

        return tool;
    }

    [McpServerToolType]
    public sealed class ArgumentValidationFixtureTool
    {
        private static int _invocationCount;

        public static int InvocationCount => Volatile.Read(ref _invocationCount);
        public static CancellationToken ReceivedToken { get; private set; }

        public static void Reset()
        {
            Interlocked.Exchange(ref _invocationCount, 0);
            ReceivedToken = default;
        }

        [McpServerTool(Name = "argument_validation_fixture")]
        public static string Validate(
            [Range(1, 5)] int count,
            [MaxLength(2)] string[] names,
            FixtureMode mode,
            int sequence = 1,
            string? note = null,
            FixtureOptions? options = null,
            int maxResponseBytes = 16_384,
            int? maxResponseTokens = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invocationCount);
            ReceivedToken = cancellationToken;
            return $"{count}:{names.Length}:{mode}:{sequence}:{note}:{options?.Label}:{maxResponseBytes}:{maxResponseTokens}";
        }

        public static string Referenced(int count)
        {
            Interlocked.Increment(ref _invocationCount);
            return count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

#pragma warning disable MEAI001 // The SDK marks this wire-name attribute as test-only; it is required to exercise the documented binder edge case.
        public static string Renamed([AIParameterName("sequence_value")] int sequence)
        {
            Interlocked.Increment(ref _invocationCount);
            return sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
#pragma warning restore MEAI001

        public static string DictionaryArray(Dictionary<string, List<FixtureOptions>> options)
        {
            Interlocked.Increment(ref _invocationCount);
            return options.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string DictionaryObject(Dictionary<string, FixtureOptions> options)
        {
            Interlocked.Increment(ref _invocationCount);
            return options.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string RootProperties(int count, string label)
        {
            Interlocked.Increment(ref _invocationCount);
            return $"{count}:{label}";
        }

        public sealed class FixtureOptions
        {
            public required string Label { get; init; }
        }

        public enum FixtureMode
        {
            Exact,
            Pattern,
        }
    }

    private sealed class FixtureSession : IAsyncDisposable
    {
        private readonly CancellationTokenSource _shutdown;
        private readonly ServiceProvider _services;
        private readonly Task _serverTask;
        private readonly McpClient _client;

        private FixtureSession(ServiceProvider services, Task serverTask, McpClient client, CancellationTokenSource shutdown)
        {
            _services = services;
            _serverTask = serverTask;
            _client = client;
            _shutdown = shutdown;
        }

        internal static async Task<FixtureSession> StartAsync(McpServerTool? tool = null)
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var shutdown = new CancellationTokenSource();
            var builder = new ServiceCollection()
                .AddMcpServer()
                .WithRequestFilters(McpArgumentValidationFilter.Configure);
            builder = tool is null
                ? builder.WithTools<ArgumentValidationFixtureTool>()
                : builder.WithTools(new[] { tool });
            var services = builder
                .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
                .Services
                .BuildServiceProvider();
            var server = services.GetRequiredService<McpServer>();
            var serverTask = server.RunAsync(shutdown.Token);
            var transport = new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream());
            var client = await McpClient.CreateAsync(transport).ConfigureAwait(false);
            return new FixtureSession(services, serverTask, client, shutdown);
        }

        internal ValueTask<CallToolResult> CallAsync(
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken = default) =>
            _client.CallToolAsync("argument_validation_fixture", arguments, cancellationToken: cancellationToken);

        internal async Task<string> GetToolsJsonAsync()
        {
            var tools = await _client.ListToolsAsync().ConfigureAwait(false);
            return tools.Single(static tool => tool.Name == "argument_validation_fixture").ProtocolTool.InputSchema.GetRawText();
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            await _shutdown.CancelAsync().ConfigureAwait(false);
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            await _services.DisposeAsync().ConfigureAwait(false);
            _shutdown.Dispose();
        }
    }
}

using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using AiNetCodeNavigator.TestKit;

namespace AiNetCodeNavigator.Exploration;

/// <summary>Add manual scenarios here, then register their names in All.</summary>
internal static class Scenarios
{
    internal static IReadOnlyDictionary<string, Func<ExplorationContext, Task>> All { get; } =
        new Dictionary<string, Func<ExplorationContext, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ExploreFindSymbol)] = ExploreFindSymbol,
            [nameof(ExploreSymbolBody)] = ExploreSymbolBody,
            [nameof(ExploreConsolidationBaseline)] = ExploreConsolidationBaseline,
            [nameof(ExploreContextUses)] = ExploreContextUses,
            [nameof(ExploreContextMembers)] = ExploreContextMembers,
            [nameof(ExploreTestHelpers)] = ExploreTestHelpers,
            [nameof(ExploreBrowseTarget)] = ExploreBrowseTarget,
            [nameof(ExploreTypeRelations)] = ExploreTypeRelations,
            [nameof(ExploreExtensionDiscovery)] = ExploreExtensionDiscovery,
            [nameof(ExploreMetadataRelations)] = ExploreMetadataRelations,
            [nameof(ExploreFocusedAssemblyOutput)] = ExploreFocusedAssemblyOutput,
            [nameof(ExploreFocusedDependencyOutput)] = ExploreFocusedDependencyOutput,
            [nameof(ExploreSourceRuntime)] = ExploreSourceRuntime,
            ["ExploreSourceRuntimeProfile"] = context => ExploreSourceRuntime(context, 1),
            [nameof(ExploreAssemblyRuntime)] = ExploreAssemblyRuntime,
            [nameof(ExploreFinalSource)] = ExploreFinalSource,
            [nameof(ExploreFinalAssembly)] = ExploreFinalAssembly,
            [nameof(ExploreTypeOriginRecovery)] = ExploreTypeOriginRecovery,
        };

    private static async Task ExploreTypeOriginRecovery(ExplorationContext context)
    {
        await RecordBuildAsync(context).ConfigureAwait(false);
        await context.CallAsync("resolve_type_origin", new
        {
            targetPath = typeof(Scenarios).Assembly.Location,
            typeName = "AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec",
        }).ConfigureAwait(false);
    }

    private static async Task ExploreFinalSource(ExplorationContext context)
    {
        await RecordBuildAsync(context).ConfigureAwait(false);
        var target = context.RepositorySolution;
        var found = await context.CallAsync("find_symbol", new
        {
            targetPath = target, pattern = "TryPrepareReferenceInput", kind = "method",
            scopeType = "production", maxResults = 10,
        }).ConfigureAwait(false);
        using var discovery = JsonDocument.Parse(found.Payload);
        var overloads = discovery.RootElement.GetProperty("results")[0].GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("docCommentId").GetString()!.StartsWith(
                "M:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec.TryPrepareReferenceInput(", StringComparison.Ordinal)).ToArray();
        if (overloads.Length != 2) throw new InvalidOperationException("Expected both codec overloads.");
        var selected = overloads.Single(entry => entry.GetProperty("docCommentId").GetString() ==
            "M:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec.TryPrepareReferenceInput(System.String,System.String,System.String@,System.Nullable{AiNetCodeNavigator.Core.Models.ResultError}@)");
        var reference = selected.GetProperty("handoffId").GetString();
        var body = await context.CallAsync("get_symbol_body", new { targetPath = target, symbolIdentifiers = new[] { reference }, startLine = 1, maxBodyLines = 20 }).ConfigureAwait(false);
        if (!body.Payload.Contains("Next body window: startLine=21, maxBodyLines=20", StringComparison.Ordinal)) throw new InvalidOperationException("Expected a real twenty-line body window.");
        await context.CallAsync("get_symbol_body", new { targetPath = target, symbolIdentifiers = new[] { reference }, startLine = 21, maxBodyLines = 20 }).ConfigureAwait(false);
        await context.CallAsync("get_file_skeleton", new { targetPath = target, filePaths = new[] { reference } }).ConfigureAwait(false);
        string? cursor = null;
        string? summary = null;
        var count = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var response = await context.CallAsync("find_references", new
            {
                targetPath = target, symbolIdentifier = reference, depth = 1, includeSummary = true,
                scopeType = "all", maxResults = 1, resultCursor = cursor,
            }).ConfigureAwait(false);
            using var page = JsonDocument.Parse(response.Payload);
            var root = page.RootElement;
            var current = root.GetProperty("summary").GetRawText();
            summary ??= current;
            if (summary != current) throw new InvalidOperationException("Reference summary changed across domain pages.");
            count += root.GetProperty("references").GetArrayLength();
            cursor = root.TryGetProperty("resultCursor", out var next) ? next.GetString() : null;
            if (cursor is not null && !seen.Add(cursor)) throw new InvalidOperationException("Repeated reference cursor.");
            if (cursor is null && count != root.GetProperty("totalCount").GetInt32()) throw new InvalidOperationException("Missing reference sites.");
        } while (cursor is not null);
        var origin = await context.CallAsync("resolve_type_origin", new { targetPath = target, symbolIdentifier = reference }).ConfigureAwait(false);
        using var source = JsonDocument.Parse(origin.Payload);
        await context.CallAsync("get_file_skeleton", new
        {
            targetPath = source.RootElement.GetProperty("targetPath").GetString(),
            filePaths = new[] { source.RootElement.GetProperty("sourceLocations")[0].GetProperty("filePath").GetString() },
        }).ConfigureAwait(false);
        foreach (var level in new[] { "type", "file", "namespace", "project" })
            await context.CallAsync("dependency_graph", new { targetPath = target, symbolIdentifier = reference, level, direction = level == "project" ? "incoming" : "outgoing", depth = 1, maxResults = 20 }).ConfigureAwait(false);
    }

    private static async Task ExploreFinalAssembly(ExplorationContext context)
    {
        await RecordBuildAsync(context).ConfigureAwait(false);
        var target = typeof(Scenarios).Assembly.Location;
        var search = await context.CallAsync("search_assembly", new
        {
            targetPath = target, pattern = "ExplorationMark", declarationOnly = true, kind = "method", maxResults = 5,
        }).ConfigureAwait(false);
        using var found = JsonDocument.Parse(search.Payload);
        var hit = found.RootElement.GetProperty("results").EnumerateArray().Single(item => item.GetProperty("handoffId").ValueKind == JsonValueKind.String);
        var owner = hit.GetProperty("ownerTargetPath").GetString();
        var reference = hit.GetProperty("handoffId").GetString();
        await context.CallAsync("get_symbol_body", new { targetPath = owner, symbolIdentifiers = new[] { reference }, maxBodyLines = 20 }).ConfigureAwait(false);
        await context.CallAsync("get_file_skeleton", new { targetPath = owner, filePaths = new[] { reference } }).ConfigureAwait(false);
        await context.CallAsync("find_references", new { targetPath = owner, symbolIdentifier = reference, depth = 1, includeSummary = true, maxResults = 5 }).ConfigureAwait(false);
        await context.CallAsync("get_context", new { targetPath = owner, symbolIdentifier = reference, sections = new[] { "body", "uses" }, maxBodyLines = 20, maxResults = 5 }).ConfigureAwait(false);
        foreach (var direction in new[] { "incoming", "outgoing" })
            await context.CallAsync("get_call_tree", new { targetPath = owner, symbolIdentifier = reference, direction, depth = 1, topN = 5, includeBcl = true }).ConfigureAwait(false);
        await context.CallAsync("browse_target", new { targetPath = target, view = "namespaces", namespacePrefix = "AiNetCodeNavigator.Exploration", depth = 1, includeTypes = true, maxResults = 20 }).ConfigureAwait(false);
        var origin = await context.CallAsync("resolve_type_origin", new { targetPath = target, typeName = "AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec" }).ConfigureAwait(false);
        using var metadata = JsonDocument.Parse(origin.Payload);
        var assemblyPath = metadata.RootElement.GetProperty("assemblyPath").GetString();
        if (metadata.RootElement.GetProperty("isAmbiguous").GetBoolean() || string.IsNullOrWhiteSpace(assemblyPath)) throw new InvalidOperationException("Expected one proven referenced Core owner.");
        var declaration = await context.CallAsync("find_symbol", new { targetPath = assemblyPath, pattern = "StableSymbolReferenceCodec", kind = "class", maxResults = 5 }).ConfigureAwait(false);
        using var symbols = JsonDocument.Parse(declaration.Payload);
        var type = symbols.RootElement.GetProperty("results")[0].GetProperty("entries").EnumerateArray()
            .Single(item => item.GetProperty("docCommentId").GetString() == "T:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec");
        await context.CallAsync("get_symbol_body", new { targetPath = type.GetProperty("ownerTargetPath").GetString(), symbolIdentifiers = new[] { type.GetProperty("handoffId").GetString() }, maxBodyLines = 20 }).ConfigureAwait(false);
        foreach (var level in new[] { "type", "file", "namespace" })
            await context.CallAsync("dependency_graph", new
            {
                targetPath = type.GetProperty("ownerTargetPath").GetString(), symbolIdentifier = type.GetProperty("handoffId").GetString(),
                level, direction = "outgoing", depth = 1, maxResults = 20,
            }).ConfigureAwait(false);
        // Deliberately missing dependency: available declarations must remain visible with truthful omissions/recovery.
        using var fixture = TestTempDirectory.Create("explore-missing-reference-");
        var dependency = AssemblyTestHelper.EmitAssembly(fixture, "W7MissingDependency", "public class MissingBase { }");
        var partialTarget = AssemblyTestHelper.EmitAssembly(fixture, "W7PartialOwner", "public class MissingRoot : MissingBase { public int Own() => 7; }", dependency);
        File.Delete(dependency);
        await context.CallAsync("search_assembly", new
        {
            targetPath = partialTarget, pattern = "Own", kind = "method", declarationOnly = true, includeDiagnostics = true, maxResults = 5,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreTestHelpers(ExplorationContext context)
    {
        using var fixture = TestTempDirectory.Create("explore-test-helpers-");
        fixture.CreateFile("W6.Tests/W6.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        fixture.CreateFile("W6.Tests/Tests.cs", """
            namespace Xunit { public sealed class FactAttribute : System.Attribute { } }
            namespace W6 {
            public static class Endpoint { public static void Run() { } }
            public static class Helpers {
                public static void One() => Endpoint.Run();
                public static void Two() => One();
                public static void Cycle() { Two(); Cycle(); }
            }
            public class Direct { [Xunit.Fact] public void Check() => Endpoint.Run(); }
            public class Indirect { [Xunit.Fact] public void Check() => Helpers.One(); }
            public class Deep { [Xunit.Fact] public void Check() => Helpers.Two(); }
            public class Cyclic { [Xunit.Fact] public void Check() => Helpers.Cycle(); }
            public class NonPath { [Xunit.Fact] public void Check() { System.Action a = Helpers.One; } }
            public class EndpointTests { [Xunit.Fact] public void NameOnly() { } }
            }
            """);
        var target = fixture.CreateFile("W6.slnx", "<Solution><Project Path=\"W6.Tests/W6.Tests.csproj\" /></Solution>");
        var followed = new HashSet<string>(StringComparer.Ordinal);
        string? selectedReference = null;
        for (var depth = 0; depth <= 2; depth++)
        {
            string? cursor = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var names = new List<string>();
            do
            {
                var response = await context.CallAsync("get_context", new
                {
                    targetPath = target, symbolIdentifier = "W6.Endpoint.Run", sections = new[] { "tests" },
                    testHelperDepth = depth, maxResults = 1, resultCursor = cursor,
                }).ConfigureAwait(false);
                using var page = JsonDocument.Parse(response.Payload);
                var root = page.RootElement;
                var reference = root.GetProperty("target").GetProperty("handoffId").GetString();
                selectedReference ??= reference;
                if (reference != selectedReference) throw new InvalidOperationException("Helper depth changed the endpoint reference.");
                var section = root.GetProperty("sections")[0];
                if (section.GetProperty("analysis").GetProperty("testHelperDepth").GetInt32() != depth)
                    throw new InvalidOperationException("Incorrect delivered helper depth.");
                foreach (var item in section.GetProperty("items").EnumerateArray())
                {
                    names.Add(item.GetProperty("className").GetString()!);
                    if (string.IsNullOrWhiteSpace(item.GetProperty("projectPath").GetString()))
                        throw new InvalidOperationException("Missing fixture project owner.");
                    foreach (var method in item.GetProperty("methods").EnumerateArray())
                    {
                        var refs = new List<string?> { method.GetProperty("handoffId").GetString() };
                        if (method.TryGetProperty("evidence", out var evidence))
                            foreach (var reason in evidence.EnumerateArray())
                                if (reason.TryGetProperty("helperPath", out var path))
                                {
                                    if (path.GetArrayLength() > depth + 1) throw new InvalidOperationException("Path exceeds requested helper depth.");
                                    foreach (var step in path.EnumerateArray())
                                    {
                                        if (step.GetProperty("relationshipKind").GetString() != "call")
                                            throw new InvalidOperationException("Fixture requires exact static invocation links.");
                                        refs.Add(step.GetProperty("callerHandoffId").GetString());
                                        refs.Add(step.GetProperty("targetHandoffId").GetString());
                                    }
                                }
                        foreach (var declaration in refs.Where(value => value is not null).Cast<string>())
                            if (followed.Add(declaration))
                                await context.CallAsync("get_symbol_body", new { targetPath = target, symbolIdentifiers = new[] { declaration }, maxBodyLines = 20 }).ConfigureAwait(false);
                    }
                }
                cursor = section.TryGetProperty("resultCursor", out var next) ? next.GetString() : null;
                if (cursor is not null && !seen.Add(cursor)) throw new InvalidOperationException("Repeated tests cursor.");
            } while (cursor is not null);
            var expected = depth switch { 0 => new[] { "Direct", "EndpointTests" }, 1 => new[] { "Direct", "Indirect", "EndpointTests" }, _ => new[] { "Direct", "Deep", "Indirect", "EndpointTests" } };
            if (!names.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)))
                throw new InvalidOperationException($"Incorrect W6 candidates at depth {depth}: {string.Join(',', names)}");
        }
    }

    private static Task ExploreSourceRuntime(ExplorationContext context) => ExploreSourceRuntime(context, 3);

    private static async Task ExploreSourceRuntime(ExplorationContext context, int bodyRuns)
    {
        var samples = new List<object>();
        var times = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        await RecordBuildAsync(context).ConfigureAwait(false);
        foreach (var (name, kind) in new[] { ("StableSymbolReference", "record class"), ("StableSymbolReferenceCodec", "class") })
        {
            var discovery = await TimedAsync("discovery-" + name, "find_symbol", new
            {
                targetPath = context.RepositorySolution, pattern = name, kind, scopeType = "production", maxResults = 10,
                maxResponseBytes = 32768, maxResponseTokens = 8192,
            }).ConfigureAwait(false);
            using var result = JsonDocument.Parse(discovery.Payload);
            var item = result.RootElement.GetProperty("results").EnumerateArray()
                .SelectMany(batch => batch.GetProperty("entries").EnumerateArray())
                .Single(entry => entry.GetProperty("docCommentId").GetString() == "T:AiNetCodeNavigator.Core.Symbols." + name);
            var reference = item.GetProperty("handoffId").GetString();
            for (var run = 1; run <= bodyRuns; run++)
                await TimedAsync("body-" + name, "get_symbol_body", new
                {
                    targetPath = context.RepositorySolution, symbolIdentifiers = new[] { reference }, startLine = 1, maxBodyLines = 20,
                    maxResponseBytes = 32768, maxResponseTokens = 8192,
                }).ConfigureAwait(false);
        }

        async Task<ExplorationResult> TimedAsync(string label, string tool, object arguments)
        {
            var watch = Stopwatch.StartNew();
            var response = await context.CallAsync(tool, arguments).ConfigureAwait(false);
            watch.Stop();
            if (!times.TryGetValue(label, out var values)) times[label] = values = [];
            values.Add(watch.Elapsed.TotalMilliseconds);
            samples.Add(new { label, run = values.Count, milliseconds = watch.Elapsed.TotalMilliseconds,
                query = arguments, elapsedIncludes = "SDK/handler, required polls/outer pages, and saved artifact writing" });
            await File.WriteAllTextAsync(Path.Combine(context.OutputDirectory, "measurements.json"),
                JsonSerializer.Serialize(new { samples, medians = times.ToDictionary(pair => pair.Key, pair => pair.Value.Order().ElementAt(pair.Value.Count / 2)) },
                    new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            return response;
        }
    }

    private static async Task ExploreAssemblyRuntime(ExplorationContext context)
    {
        await RecordBuildAsync(context).ConfigureAwait(false);
        var target = typeof(Core.Symbols.StableSymbolReferenceCodec).Assembly.Location;
        var samples = new List<object>();
        var times = new List<double>();
        string? selectedOwner = null;
        string? selectedReference = null;
        foreach (var (references, label) in new[] { (true, "cold-closure"), (false, "warm-root-only"), (true, "warm-closure"), (true, "warm-closure"), (true, "warm-closure") })
        {
            if (label == "warm-closure" && selectedReference is null) break;
            var query = new { targetPath = target, extensionOnly = true, receiverType = "System.String", includeReferences = references,
                maxResults = 5, includeDiagnostics = true, maxResponseBytes = 32768, maxResponseTokens = 8192 };
            var watch = Stopwatch.StartNew();
            var response = await context.CallAsync("find_symbol", query).ConfigureAwait(false);
            watch.Stop();
            if (label == "warm-closure") times.Add(watch.Elapsed.TotalMilliseconds);
            using var page = JsonDocument.Parse(response.Payload);
            var entries = page.RootElement.GetProperty("results")[0].GetProperty("entries").EnumerateArray().ToArray();
            if (references && entries.Length > 0)
            {
                var entry = entries.First(hit => hit.TryGetProperty("handoffId", out _));
                selectedOwner ??= entry.GetProperty("ownerTargetPath").GetString();
                selectedReference ??= entry.GetProperty("handoffId").GetString();
            }
            samples.Add(new { label, run = label == "warm-closure" ? times.Count : 1, milliseconds = watch.Elapsed.TotalMilliseconds, query,
                totalMatches = page.RootElement.GetProperty("results")[0].GetProperty("totalMatches").GetInt32(),
                elapsedIncludes = "SDK/handler, required polls/outer pages, and saved artifact writing" });
            await File.WriteAllTextAsync(Path.Combine(context.OutputDirectory, "measurements.json"),
                JsonSerializer.Serialize(new { samples, warmMedianMilliseconds = times.Count == 0 ? (double?)null : times.Order().ElementAt(times.Count / 2),
                    runtimePolicy = "Fresh runtime; existing persistent decompilation disk cache retained" },
                    new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        }
        if (selectedReference is null)
        {
            // Separate known declaration/owner follow-up; this does not turn the empty Core closure into capability evidence.
            var known = await context.CallAsync("find_symbol", new
            {
                targetPath = typeof(Scenarios).Assembly.Location, pattern = "*Exploration*", extensionOnly = true,
                receiverType = "System.String", includeReferences = false, maxResults = 5,
                maxResponseBytes = 32768, maxResponseTokens = 8192,
            }).ConfigureAwait(false);
            using var knownPage = JsonDocument.Parse(known.Payload);
            var entry = knownPage.RootElement.GetProperty("results")[0].GetProperty("entries").EnumerateArray()
                .First(hit => hit.GetProperty("name").GetString() == "ExplorationMark");
            selectedOwner = entry.GetProperty("ownerTargetPath").GetString();
            selectedReference = entry.GetProperty("handoffId").GetString();
        }
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = selectedOwner, symbolIdentifiers = new[] { selectedReference }, startLine = 1, maxBodyLines = 20,
            maxResponseBytes = 32768, maxResponseTokens = 8192,
        }).ConfigureAwait(false);
    }

    private static async Task RecordBuildAsync(ExplorationContext context)
    {
        var assemblies = new[] { typeof(Scenarios).Assembly, typeof(Core.Symbols.StableSymbolReferenceCodec).Assembly, typeof(Mcp.NavigatorHostRuntime).Assembly };
        await File.WriteAllTextAsync(Path.Combine(context.OutputDirectory, "build.json"),
            JsonSerializer.Serialize(assemblies.Select(assembly => new { assembly.FullName, path = assembly.Location,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) }),
                new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
    }

    private static async Task ExploreFocusedAssemblyOutput(ExplorationContext context)
    {
        const string codec = "AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec";
        var target = typeof(Core.Symbols.StableSymbolReferenceCodec).Assembly.Location;
        var compact = await context.CallAsync("inspect_assembly", new
        {
            targetPath = target, typeName = codec, exactTypeName = true, publicOnly = true, maxResults = 10,
        }).ConfigureAwait(false);
        using var compactJson = JsonDocument.Parse(compact.Payload);
        var type = compactJson.RootElement.GetProperty("types").EnumerateArray().Single();
        if (type.TryGetProperty("members", out _)) throw new InvalidOperationException("Compact overview collected member details.");
        var owner = type.GetProperty("ownerTargetPath").GetString();
        var reference = type.GetProperty("handoffId").GetString();
        var detail = await context.CallAsync("inspect_assembly", new
        {
            targetPath = target, typeName = codec, exactTypeName = true, publicOnly = true, maxResults = 10, includeMembers = true,
        }).ConfigureAwait(false);
        using var detailJson = JsonDocument.Parse(detail.Payload);
        var detailedType = detailJson.RootElement.GetProperty("types").EnumerateArray().Single();
        if (detailedType.GetProperty("handoffId").GetString() != reference || detailedType.GetProperty("ownerTargetPath").GetString() != owner)
            throw new InvalidOperationException("Detail query changed its returned owner/reference.");
        var selectedReferences = detailedType.GetProperty("members").EnumerateArray()
            .Where(member => member.TryGetProperty("handoffId", out _)).Select(member => member.GetProperty("handoffId").GetString()).ToHashSet();
        string? cursor = null;
        string? memberReference = null;
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var members = await context.CallAsync("get_context", new
            {
                targetPath = owner, symbolIdentifier = reference, sections = new[] { "members" },
                memberNameFilter = "TryParse", memberKindFilter = "Method", maxResults = 1, resultCursor = cursor,
            }).ConfigureAwait(false);
            using var page = JsonDocument.Parse(members.Payload);
            var section = page.RootElement.GetProperty("sections")[0];
            foreach (var member in section.GetProperty("items").EnumerateArray())
                if (member.TryGetProperty("handoffId", out var handoff) && selectedReferences.Contains(handoff.GetString()))
                    memberReference ??= handoff.GetString();
            cursor = section.TryGetProperty("resultCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            if (cursor is not null && !cursors.Add(cursor)) throw new InvalidOperationException("Member cursor repeated.");
        } while (cursor is not null);
        if (memberReference is null) throw new InvalidOperationException("Expected an exact public member shared by inspection and focused context.");
        await context.CallAsync("get_symbol_body", new { targetPath = owner, symbolIdentifiers = new[] { memberReference }, maxBodyLines = 20 }).ConfigureAwait(false);
        await context.CallAsync("get_symbol_body", new { targetPath = owner, symbolIdentifiers = new[] { reference }, maxBodyLines = 20 }).ConfigureAwait(false);
        await context.CallAsync("get_symbol_body", new { targetPath = owner, symbolIdentifiers = new[] { reference }, startLine = 21, maxBodyLines = 20 }).ConfigureAwait(false);
        await ExploreFocusedDependencyOutput(context).ConfigureAwait(false);
    }

    private static async Task ExploreFocusedDependencyOutput(ExplorationContext context)
    {
        await context.CallAsync("dependency_graph", new
        {
            targetPath = context.RepositorySolution,
            symbolIdentifier = "src:src/AiNetCodeNavigator.Core/AiNetCodeNavigator.Core.csproj|T:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec",
            direction = "outgoing", depth = 1, scopeType = "production", maxResults = 50,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreExtensionDiscovery(ExplorationContext context)
    {
        foreach (var target in new[] { context.RepositorySolution, typeof(Scenarios).Assembly.Location })
        {
            string? cursor = null;
            do
            {
                var response = await context.CallAsync("find_symbol", new
                {
                    targetPath = target, extensionOnly = true, receiverType = "global::System.String",
                    pattern = "*Exploration*", namespaceFilter = "AINETCODENAVIGATOR.EXPLORATION", signatureFilter = "Exploration",
                    maxResults = 1, resultCursor = cursor,
                }).ConfigureAwait(false);
                using var page = JsonDocument.Parse(response.Payload);
                var items = page.RootElement.GetProperty("results")[0].GetProperty("entries");
                foreach (var item in items.EnumerateArray())
                    await context.CallAsync("get_symbol_body", new
                    {
                        targetPath = item.TryGetProperty("ownerTargetPath", out var owner) ? owner.GetString() : target,
                        symbolIdentifiers = new[] { item.GetProperty("handoffId").GetString() }, maxBodyLines = 10,
                    }).ConfigureAwait(false);
                cursor = page.RootElement.TryGetProperty("resultCursor", out var next) ? next.GetString() : null;
            } while (cursor is not null);
        }
    }

    private static async Task ExploreTypeRelations(ExplorationContext context)
    {
        // Declared runner fixtures exercise real Roslyn mappings and assembly ownership.
        var target = context.RepositorySolution;
        var identifier = "T:AiNetCodeNavigator.Exploration.IExplorationRelationProbe";
        foreach (var relation in new[] { "hierarchy", "implementations" })
        {
            string? cursor = null;
            do
            {
                var response = await context.CallAsync("get_type_relations", new
                {
                    targetPath = target, symbolIdentifier = identifier, relation, maxResults = 1, resultCursor = cursor,
                }).ConfigureAwait(false);
                using var page = JsonDocument.Parse(response.Payload);
                var root = page.RootElement;
                var items = root.GetProperty(relation == "hierarchy" ? "subtypes" : "implementations");
                if (cursor is null && items.GetArrayLength() > 0)
                    await context.CallAsync("get_symbol_body", new
                    {
                        targetPath = target, symbolIdentifiers = new[] { items[0].GetProperty("handoffId").GetString() }, maxBodyLines = 10,
                    }).ConfigureAwait(false);
                cursor = root.TryGetProperty("resultCursor", out var next) ? next.GetString() : null;
            } while (cursor is not null);
        }
        var assemblyTarget = typeof(Scenarios).Assembly.Location;
        foreach (var relation in new[] { "hierarchy", "implementations" })
        {
            string? cursor = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var response = await context.CallAsync("get_type_relations", new
                {
                    targetPath = assemblyTarget, symbolIdentifier = identifier, relation, maxResults = 1, resultCursor = cursor,
                }).ConfigureAwait(false);
                using var page = JsonDocument.Parse(response.Payload);
                var items = page.RootElement.GetProperty(relation == "hierarchy" ? "subtypes" : "implementations");
                if (items.GetArrayLength() > 0)
                    await context.CallAsync("get_symbol_body", new
                    {
                        targetPath = assemblyTarget, symbolIdentifiers = new[] { items[0].GetProperty("handoffId").GetString() }, maxBodyLines = 10,
                    }).ConfigureAwait(false);
                cursor = page.RootElement.TryGetProperty("resultCursor", out var next) ? next.GetString() : null;
                if (cursor is not null && !seen.Add(cursor)) throw new InvalidOperationException("Repeated assembly relation cursor.");
            } while (cursor is not null);
        }
    }

    private static async Task ExploreMetadataRelations(ExplorationContext context)
    {
        using var first = TestTempDirectory.Create("explore-metadata-first-");
        using var second = TestTempDirectory.Create("explore-metadata-second-");
        const string api = "namespace W5.Contracts; public interface IService { int Read(); }";
        var firstOwner = AssemblyTestHelper.EmitAssembly(first, "W5Contracts", api);
        var secondOwner = AssemblyTestHelper.EmitAssembly(second, "W5Contracts", api + " public class OtherImage { }");
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include=\"W5Contracts\"><HintPath>{0}</HintPath></Reference></ItemGroup></Project>";
        first.CreateFile("One/One.csproj", string.Format(System.Globalization.CultureInfo.InvariantCulture, project, firstOwner));
        first.CreateFile("Two/Two.csproj", string.Format(System.Globalization.CultureInfo.InvariantCulture, project, secondOwner));
        first.CreateFile("One/Worker.cs", "namespace W5; public class Worker : Contracts.IService, System.IDisposable { public int Read() => 1; public void Dispose() { } } public class DerivedWorker : Worker { }");
        first.CreateFile("Two/Worker.cs", "namespace W5; public class OtherWorker : Contracts.IService, System.IDisposable { public int Read() => 2; public void Dispose() { } }");
        var target = first.CreateFile("W5.slnx", "<Solution><Project Path=\"One/One.csproj\" /><Project Path=\"Two/Two.csproj\" /></Solution>");
        var ambiguous = await context.CallAsync("get_type_relations", new
        {
            targetPath = target, symbolIdentifier = "W5.Contracts.IService", relation = "implementations", maxResults = 1,
        }, expectedErrorCode: "AMBIGUOUS_SYMBOL").ConfigureAwait(false);
        var candidateText = ambiguous.Text[(ambiguous.Text.IndexOf("Candidates: ", StringComparison.Ordinal) + "Candidates: ".Length)..];
        using var candidates = JsonDocument.Parse(candidateText[..(candidateText.IndexOf(']') + 1)]);
        var selectedOwner = candidates.RootElement.EnumerateArray().Select(candidate => candidate.GetProperty("metadataOwnerPath").GetString())
            .Single(path => string.Equals(path, firstOwner, StringComparison.OrdinalIgnoreCase));
        foreach (var relation in new[] { "hierarchy", "implementations" })
        {
            string? cursor = null;
            do
            {
                var response = await context.CallAsync("get_type_relations", new
                {
                    targetPath = target, symbolIdentifier = "W5.Contracts.IService", relation, metadataOwnerPath = selectedOwner,
                    maxResults = 1, resultCursor = cursor,
                }).ConfigureAwait(false);
                using var page = JsonDocument.Parse(response.Payload);
                var root = page.RootElement;
                if (cursor is null)
                {
                    var external = root.GetProperty("metadataRoot");
                    await context.CallAsync("get_symbol_body", new
                    {
                        targetPath = external.GetProperty("ownerTargetPath").GetString(),
                        symbolIdentifiers = new[] { external.GetProperty("handoffId").GetString() }, maxBodyLines = 10,
                    }).ConfigureAwait(false);
                }
                foreach (var item in root.GetProperty(relation == "hierarchy" ? "subtypes" : "implementations").EnumerateArray())
                    await context.CallAsync("get_symbol_body", new
                    {
                        targetPath = target, symbolIdentifiers = new[] { item.GetProperty("handoffId").GetString() }, maxBodyLines = 10,
                    }).ConfigureAwait(false);
                cursor = root.TryGetProperty("resultCursor", out var next) ? next.GetString() : null;
            } while (cursor is not null);
        }
        var bcl = await context.CallAsync("get_type_relations", new
        {
            targetPath = target, symbolIdentifier = "M:System.IDisposable.Dispose", relation = "implementations", maxResults = 10,
        }).ConfigureAwait(false);
        using var bclPage = JsonDocument.Parse(bcl.Payload);
        foreach (var item in bclPage.RootElement.GetProperty("implementations").EnumerateArray())
            await context.CallAsync("get_symbol_body", new
            {
                targetPath = target, symbolIdentifiers = new[] { item.GetProperty("handoffId").GetString() }, maxBodyLines = 10,
            }).ConfigureAwait(false);
    }

    private static async Task ExploreFindSymbol(ExplorationContext context)
    {
        await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution,
            pattern = "NavigatorHostRuntime",
            kind = "class",
            scopeType = "production",
            maxResults = 10,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreConsolidationBaseline(ExplorationContext context)
    {
        var discovery = await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution,
            pattern = "StableSymbolReferenceCodec",
            kind = "class",
            scopeType = "production",
            maxResults = 10,
        }).ConfigureAwait(false);
        using var source = JsonDocument.Parse(discovery.Payload);
        var reference = source.RootElement.GetProperty("results")[0].GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("docCommentId").GetString() == "T:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec")
            .GetProperty("handoffId").GetString();
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = context.RepositorySolution, symbolIdentifiers = new[] { reference }, maxBodyLines = 20,
        }).ConfigureAwait(false);
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = context.RepositorySolution, symbolIdentifiers = new[] { reference }, startLine = 21, maxBodyLines = 20,
        }).ConfigureAwait(false);
        await context.CallAsync("dependency_graph", new
        {
            targetPath = context.RepositorySolution, symbolIdentifier = reference,
            direction = "outgoing", depth = 1, scopeType = "production", maxResults = 50,
        }).ConfigureAwait(false);
        var callers = await context.CallAsync("get_context", new
        {
            targetPath = context.RepositorySolution, symbolIdentifier = reference, sections = new[] { "uses" }, maxResults = 5,
        }).ConfigureAwait(false);
        using (var callerPage = JsonDocument.Parse(callers.Payload))
        {
            if (callerPage.RootElement.GetProperty("sections")[0].TryGetProperty("resultCursor", out var cursor))
                await context.CallAsync("get_context", new
                {
                    targetPath = context.RepositorySolution, symbolIdentifier = reference,
                    sections = new[] { "uses" }, maxResults = 5, resultCursor = cursor.GetString(),
                }).ConfigureAwait(false);
        }
        var assemblyTarget = typeof(Core.Symbols.StableSymbolReferenceCodec).Assembly.Location;
        var inspection = await context.CallAsync("inspect_assembly", new
        {
            targetPath = assemblyTarget, typeName = "AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec",
            exactTypeName = true, publicOnly = true, maxResults = 10,
        }).ConfigureAwait(false);
        using var assembly = JsonDocument.Parse(inspection.Payload);
        var type = assembly.RootElement.GetProperty("types").EnumerateArray().Single();
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = type.GetProperty("ownerTargetPath").GetString(),
            symbolIdentifiers = new[] { type.GetProperty("handoffId").GetString() }, maxBodyLines = 20,
        }).ConfigureAwait(false);
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = type.GetProperty("ownerTargetPath").GetString(),
            symbolIdentifiers = new[] { type.GetProperty("handoffId").GetString() }, startLine = 21, maxBodyLines = 20,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreContextUses(ExplorationContext context)
    {
        var discovery = await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution, pattern = "TryParse", kind = "method",
            scopeType = "production", maxResults = 20,
        }).ConfigureAwait(false);
        using var symbols = JsonDocument.Parse(discovery.Payload);
        var reference = symbols.RootElement.GetProperty("results").EnumerateArray()
            .SelectMany(result => result.GetProperty("entries").EnumerateArray())
            .Single(entry => entry.GetProperty("docCommentId").GetString()!.StartsWith(
                "M:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec.TryParse(", StringComparison.Ordinal))
            .GetProperty("handoffId").GetString();
        var result = await context.CallAsync("get_context", new
        {
            targetPath = context.RepositorySolution, symbolIdentifier = reference,
            sections = new[] { "body", "uses" }, usageScope = "all", maxBodyLines = 20, maxResults = 5,
        }).ConfigureAwait(false);
        using var page = JsonDocument.Parse(result.Payload);
        var uses = page.RootElement.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("name").GetString() == "uses");
        if (uses.TryGetProperty("resultCursor", out var cursor))
            await context.CallAsync("get_context", new
            {
                targetPath = context.RepositorySolution, symbolIdentifier = reference,
                sections = new[] { "body", "uses" }, usageScope = "all", maxBodyLines = 20,
                maxResults = 5, resultCursor = cursor.GetString(),
            }).ConfigureAwait(false);
        var firstUse = uses.GetProperty("items").EnumerateArray()
            .First(site => site.TryGetProperty("enclosingSymbolHandoffId", out var handoff) && handoff.ValueKind == JsonValueKind.String);
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = page.RootElement.GetProperty("target").GetProperty("ownerTargetPath").GetString(),
            symbolIdentifiers = new[] { firstUse.GetProperty("enclosingSymbolHandoffId").GetString() }, maxBodyLines = 20,
        }).ConfigureAwait(false);
        foreach (var direction in new[] { "incoming", "outgoing" })
            await context.CallAsync("get_call_tree", new
            {
                targetPath = context.RepositorySolution, symbolIdentifier = reference, direction,
                depth = 1, topN = 5, scopeType = "production",
            }).ConfigureAwait(false);
    }

    private static async Task ExploreContextMembers(ExplorationContext context)
    {
        var discovery = await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution, pattern = "StableSymbolReferenceCodec",
            kind = "class", scopeType = "production", maxResults = 10,
        }).ConfigureAwait(false);
        using var found = JsonDocument.Parse(discovery.Payload);
        var reference = found.RootElement.GetProperty("results")[0].GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("docCommentId").GetString() == "T:AiNetCodeNavigator.Core.Symbols.StableSymbolReferenceCodec")
            .GetProperty("handoffId").GetString();
        string? cursor = null;
        string? memberReference = null;
        string? owner = null;
        do
        {
            var response = await context.CallAsync("get_context", new
            {
                targetPath = context.RepositorySolution, symbolIdentifier = reference, sections = new[] { "members" },
                memberNameFilter = "Parse", memberKindFilter = "Method", memberSortBy = "name", memberScope = "production",
                maxResults = 1, resultCursor = cursor,
            }).ConfigureAwait(false);
            using var page = JsonDocument.Parse(response.Payload);
            var section = page.RootElement.GetProperty("sections")[0];
            owner ??= section.GetProperty("structure").GetProperty("targetPath").GetString();
            foreach (var member in section.GetProperty("items").EnumerateArray())
                memberReference ??= member.GetProperty("handoffId").GetString();
            cursor = section.TryGetProperty("resultCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        } while (cursor is not null);
        if (memberReference is null || owner is null) throw new InvalidOperationException("Expected a navigable filtered codec member.");
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = owner, symbolIdentifiers = new[] { memberReference }, maxBodyLines = 20,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreBrowseTarget(ExplorationContext context)
    {
        var scope = await context.CallAsync("browse_target", new
        {
            targetPath = context.RepositorySolution, view = "scope", maxResults = 100,
        }).ConfigureAwait(false);
        using var inventory = JsonDocument.Parse(scope.Payload);
        var projectPath = inventory.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "project" && item.GetProperty("name").GetString() == "AiNetCodeNavigator.Core")
            .GetProperty("projectPath").GetString();
        var namespaces = await context.CallAsync("browse_target", new
        {
            targetPath = context.RepositorySolution, view = "namespaces", project = projectPath,
            namespacePrefix = "AiNetCodeNavigator.Core.Models", depth = 1, maxResults = 50,
        }).ConfigureAwait(false);
        using var types = JsonDocument.Parse(namespaces.Payload);
        var reference = types.RootElement.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("kind").GetString() == "type" && item.TryGetProperty("handoffId", out _))
            .GetProperty("handoffId").GetString();
        if (reference is null) throw new InvalidOperationException("Expected a navigable source type in the selected project/prefix.");
        await context.CallAsync("get_symbol_body", new
        {
            targetPath = context.RepositorySolution, symbolIdentifiers = new[] { reference }, maxBodyLines = 20,
        }).ConfigureAwait(false);
    }

    private static async Task ExploreSymbolBody(ExplorationContext context)
    {
        var discovery = await context.CallAsync("find_symbol", new
        {
            targetPath = context.RepositorySolution,
            pattern = "NavigatorHostRuntime",
            kind = "class",
            scopeType = "production",
            maxResults = 10,
        }).ConfigureAwait(false);
        using var document = JsonDocument.Parse(discovery.Payload);
        var references = document.RootElement.GetProperty("results").EnumerateArray()
            .SelectMany(result => result.GetProperty("entries").EnumerateArray())
            .Where(entry => entry.GetProperty("docCommentId").GetString() == "T:AiNetCodeNavigator.Mcp.NavigatorHostRuntime")
            .Select(entry => entry.GetProperty("handoffId").GetString()).Where(reference => reference is not null).ToArray();
        // Discovery output remains a useful experiment if the declaration moves or disappears.
        if (references.Length != 1)
        {
            await File.WriteAllTextAsync(Path.Combine(context.OutputDirectory, "note.txt"),
                "Body follow-up skipped: discovery did not return one exact NavigatorHostRuntime reference. Inspect discovery output.").ConfigureAwait(false);
            return;
        }

        await context.CallAsync("get_symbol_body", new
        {
            targetPath = context.RepositorySolution,
            symbolIdentifiers = references,
            maxBodyLines = 100,
        }).ConfigureAwait(false);
    }
}

internal interface IExplorationRelationProbe
{
    int Read();
}

internal sealed class ExplorationRelationFirst : IExplorationRelationProbe
{
    public int Read() => 1;
}

internal sealed class ExplorationRelationSecond : IExplorationRelationProbe
{
    int IExplorationRelationProbe.Read() => 2;
}

internal static class ExplorationStringExtensions
{
    internal static int ExplorationMark(this string value) => value.Length;
    internal static int ExplorationSize(this string value) => value.ExplorationMark() + 1;
}

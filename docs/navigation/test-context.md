# Test Context

`TestDetector` classifies test projects, files, classes, and methods from test-framework references, project/file naming patterns, source paths, and recognized test attributes. Relative source paths rooted at `test/` or `tests/` count as test files, along with matching segments inside longer paths. Framework metadata recognizes xUnit, NUnit, MSTest, and the test platform; assertion and mocking libraries alone do not make a project a test project. Suffix matching respects identifier boundaries so ordinary names such as `Latest.cs` and `Contest` are not classified from the substring `Test`.

`TestRecommendationBuilder.BuildAsync` searches solution projects for fixture names formed from the target type name and common test prefixes or suffixes. It returns matching fixture and attributed test-method locations with the source project name and optional source handoffs. Same-named fixtures from separate projects remain separate candidates, and the result order is deterministic. Recognized method attributes identify xUnit (`Fact`/`Theory`), NUnit (`Test`/`TestCase`), and MSTest (`TestMethod`/`DataTestMethod`); MSTest's `TestMethodAttribute` is included in both framework classification and attributed-method collection. A name-only match without a recognized framework attribute reports `Unknown`.

`TestContextPayload.EvidenceMode` is always `static-test-candidates-only`. These results are static heuristic candidates. A name or path match does not establish that a test is related to the target, that it runs, or that it covers the target. Validate candidates before treating them as test evidence.

The public `get_test_context` handler defaults to 30 candidates, supports `scopeType` and `includeGenerated` (false by default), and filters before applying the limit. Its source result is exercised over stdio. Public response-budget and reference parity remain under verification; see [MCP navigation registration status](mcp-registration-status.md).

`BuildAsync` requires a non-null target symbol and solution and throws `ArgumentNullException` when either is missing.

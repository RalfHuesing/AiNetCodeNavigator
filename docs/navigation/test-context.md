# Test Context

`TestDetector` classifies test projects, files, classes, and methods from test-framework references, project/file naming patterns, source paths, and recognized test attributes. Framework metadata recognizes xUnit, NUnit, MSTest, and the test platform; assertion and mocking libraries alone do not make a project a test project. Suffix matching respects identifier boundaries so ordinary names such as `Latest.cs` and `Contest` are not classified from the substring `Test`.

`TestRecommendationBuilder.BuildAsync` searches solution projects for fixture names formed from the target type name and common test prefixes or suffixes. It returns matching fixture and attributed test-method locations with optional source handoffs. Recognized method attributes identify xUnit (`Fact`/`Theory`), NUnit (`Test`/`TestCase`), and MSTest (`TestMethod`/`DataTestMethod`); a name-only match without a recognized framework attribute reports `Unknown`.

These results are static heuristic candidates. A name or path match does not establish that a test is related to the target, that it runs, or that it covers the target. Validate candidates before treating them as test evidence.

`BuildAsync` requires a non-null target symbol and solution and throws `ArgumentNullException` when either is missing.

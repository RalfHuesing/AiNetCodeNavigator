# Cluster 4 Review Log

Reference policy: external comparison material has been removed from this historical record. Local documentation, the public contract matrix, and Navigator code and tests are authoritative. Commit IDs, findings, and reported historical gate results below retain their original provenance; this cleanup does not rerun or reaccept them.

## Point 4.1 implementation

- Base commit: `d821f8bc89e60c3bd0b1d75e4ac7eddf60a9752a`; the working tree was clean before this slice.
- Name and wildcard matching and `SymbolKind` filtering were present.
- **P2 implementation finding resolved — project scope was inferred from symbol/file names.** A plain `SharedType` declaration in a project named `App.Tests`, with a non-test path and filename, appeared in both `production` and `tests` results. The new regression initially failed with two production entries. The scanner now classifies each source location using its Roslyn project and document path, then applies `all`, `production`, or `tests` to those locations. The regression verifies separate same-named declarations in production and test projects, plus inclusion in `all`.
- Existing FastTests cover exact-name and wildcard matching and class/method kind filtering. No changes were needed for those filters.
- Added current-state documentation at `docs/navigation/find-symbol.md` and linked it from `docs/README.md`.
- Point audit remains open as requested. This implementation record is not an independent audit; the checkbox in `Clusters/Cluster-04.md` remains `[ ]`.

### Verification

| Gate | Result |
|---|---|
| Regression before fix | Failed as expected: production scope returned both projects |
| Focused regression after fix | Passed, 1/1 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 268/268 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 280/280 across both test projects |
| `git diff --check` | Passed before commit |

## Point 4.1 audit finding fixes

- Base commit: `71c9e5732eac76b4af111887739e163ac3648971`; working tree was clean before this slice.

- **Kind filter gaps fixed.** The previous `struct` filter also returned record structs, and the request vocabulary lacked `delegate`, `record class`, and `record struct`. Added these filters, made plain `struct` exclude record structs, and made the entry kind identify record classes and record structs separately. A regression failed on the original implementation because it returned both a plain struct and a record struct for the `struct` query; new coverage checks delegate and all record query forms.
- **Generated source contract added.** `FindSymbolScanRequest.IncludeGenerated` defaults to `false`. The scanner filters source locations by project scope and generated status. It recognizes `obj/`, `.g.cs`, `.g.i.cs`, `.generated.cs`, `.designer.cs`, auto-generated headers within the first five lines, and `GeneratedCodeAttribute`. `IncludeGenerated: true` enables those locations under `all`, `production`, or `tests`. A mixed partial declaration retains only its editable location by default and both locations when generated code is requested. Tests exercise path, header, attribute, production/test scopes, and mixed declarations.
- Updated the current-state Find Symbol page and Cluster 4 implementation checklist. The independent point audit checkbox remains `[ ]`; this fix record is not the independent audit.

### Verification

| Gate | Result |
|---|---|
| Regression before fix: plain `struct` filter | Failed as expected: also returned a record struct |
| Focused FindSymbolScanner FastTests after fix | Passed, 11/11 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 271/271 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 283/283 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 4.1

- Reviewed commit: `bc1da65c893eb3686b7205e2660950bd016a1c9a` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- The project/document scope fix and the existing exact name, wildcard, class/method, multi-project, and truncation tests support the checked implementation items. This review did not run a build or tests; the implementation gates above belong to the earlier slice.

### Open findings

1. **P2 — The kind filter does not cover the required public kind vocabulary.** `FindSymbolModels.cs:17-29` has no `delegate`, `record class`, or `record struct` values. `FindSymbolScanner.cs:222-236` also admits record structs for plain `struct`, but the required kind distinction separates records from plain `class`/`struct` and recognizes record kinds and delegates. The local kind test (`FindSymbolScannerTests.cs:262-275`) covers only class versus method. **Acceptance:** expose and correctly filter all required kind values, preserve distinct record class/struct results, and test positive and mismatched cases, including a record struct excluded by plain `struct`.
2. **P2 — Generated declarations are returned by default with no opt-in control.** `FindSymbolScanRequest` (`FindSymbolModels.cs:49-56`) has no `IncludeGenerated` option, and `FindSymbolScanner.CollectVisibleLocations` (`FindSymbolScanner.cs:158-185`) admits every source location that passes the project scope. The acceptance requirement is to exclude generated locations unless `IncludeGenerated` is true. The local tests have no generated-file or generated-header case. **Acceptance:** classify generated locations using explicit path, header, and attribute rules, exclude them by default for `all`, `production`, and `tests`, allow explicit inclusion, and cover both generated path/header and a declaration with generated and editable locations.

The point 4.1 audit checkbox remains open pending fixes and a follow-up audit. No product code or external repository was changed in this audit.

## Independent audit 2/3 of point 4.1

- Reviewed commit: `ce047c598178beb5c0824faa668523fe88af6bae` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- The second implementation slice's gate results are recorded above; this audit did not run a build or tests.

### Finding disposition

1. **P2 kind vocabulary — closed.** `FindSymbolModels.cs` now includes delegate and both specific record forms. `FindSymbolScanner.FilterByKind` distinguishes plain class/struct from record class/struct, while generic record includes both. `DescribeKind` returns the specific record form. New FastTests check delegate, generic and specific record results, mismatches, and record struct exclusion from plain struct. The plain class branch also explicitly excludes records. No kind-filter regression was found in the reviewed paths.
2. **P2 generated-source filtering — closed.** `FindSymbolScanRequest.IncludeGenerated` defaults to `false`. `CollectVisibleLocationsAsync` filters each source location after project/document scope classification. `IsGeneratedDocumentAsync` checks path suffixes, the `obj/` segment, the first-five-line header, and declared-symbol attributes. New FastTests cover generated path variants, header, attribute, explicit inclusion in production and test scopes, and an editable/generated partial declaration. The existing scope and multi-project tests remain present.

No open finding remains for point 4.1 on this reviewed Core implementation. The audit checkbox is complete after two audits. The end-to-end MCP contract remains subject to Cluster 11 verification.

## Point 4.2 implementation

- Base commit: `d7f6e52facf480573bb0db4390dba618ce5b2537`; the working tree was clean before this slice.
- The resolver uses AST declaration text with a one-based `startLine`/`maxBodyLines` window; its tests cover truncation and out-of-range windows. The existing Core code had the same basic approach but did not cover partial-method implementations, executable default interface members, null inputs, source-less metadata symbols, or mixed availability in a batch.
- **P2 — Partial method lookup returned its declaration instead of its body.** A regression for `partial void Execute(); partial void Execute() { Runs++; }` failed before the fix: resolving the declared method returned only `partial void Execute();`. `Resolve` now selects the implementation syntax reference when Roslyn exposes a partial implementation. A partial definition with no implementation reports `unavailable` and a specific hint.
- **P2 — Executable default interface methods were marked unavailable.** The previous availability test treated every symbol under an interface as bodyless. The regression failed before the fix for an interface method with a concrete body. Availability now treats the interface type declaration itself as non-executable while relying on method/property/event body metadata for members.
- Added null argument validation for `Resolve` and `ResolveBatch`. Tests cover metadata-only symbols as recoverable unavailable results, empty and ordered mixed batches, non-positive line-window normalization, and an out-of-range start line. Added [current-state documentation](../../../docs/navigation/get-symbol-body.md) and indexed it in `docs/README.md`.
- The independent point 4.2 audit checkbox remains `[ ]`; this implementation record is not the independent audit.

### Verification

| Gate | Result |
|---|---|
| Partial-method regression before fix | Failed as expected: returned the body-free declaration |
| Default-interface-method regression before fix | Failed as expected: marked the executable body unavailable |
| Focused GetSymbolBody FastTests after fix | Passed, 13/13 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 280/280 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 292/292 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 4.2

- Reviewed commit: `810d6298d1741df8235928817fab09706f1df9c8` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- The implementation slice's gate results are recorded separately. This audit did not run a build or tests.

### Findings and acceptance

- **No open finding for point 4.2.** `GetBodySyntaxReference` selects a partial method implementation when available, and `HasUnavailableBody` distinguishes a bodyless partial definition or abstract interface member from an executable default interface method. `Extract` uses a one-based, normalized line window and explicit continuation behavior. `ResolveBatch` projects the input in order and keeps metadata-only symbols as unavailable results; an empty input returns an empty list. FastTests cover those cases, ordinary and abstract members, null direct inputs, and out-of-range windows. The reviewed Core contract meets the listed point 4.2 acceptance criteria.
- The future MCP tool contract, identifier resolution, and real transport remain subject to their later roadmap points and Cluster 11 end-to-end verification.

The point 4.2 audit checkbox is complete after one audit. No product code or external repository was changed in this audit.

## Point 4.3 implementation

- Base commit: `0477f10b6af936ecea10070ad0f3defc37775421`; the working tree was clean before this slice.

- **P2 — Nested block namespaces lost their containing namespace.** A regression using `namespace Outer { namespace Inner { public class NestedType { } } }` failed before the fix: the produced namespace was `Inner`, while the symbol is in `Outer.Inner`. The walker now composes namespace segments and restores the enclosing namespace after each block.
- **P2 — Public Core entry points leaked null dereferences.** Focused regressions failed before the fix for null document/project/render inputs: they raised `NullReferenceException` instead of identifying the invalid argument. The file and project builders, Markdown renderer, and syntax walker now reject null required arguments with `ArgumentNullException`.
- Added the current-state page [Get File Skeleton](../../../docs/navigation/get-file-skeleton.md), indexed it in `docs/README.md`, and expanded FastTests for nested namespaces, null inputs, signature/body separation, Markdown output, and a type handoff roundtrip. The independent 4.3 audit checkbox remains `[ ]`; this implementation record is not an independent audit.

### Verification

| Gate | Result |
|---|---|
| Nested namespace and null-argument regressions before fix | Failed as expected: namespace dropped outer segment; invalid arguments raised `NullReferenceException` |
| Focused Skeleton FastTests after fix | Passed, 14/14 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 289/289 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 301/301 across both test projects |
| `git diff --check` | Passed before commit |

## Point 4.3 audit finding fixes

- Base commit: `8da62214e67a6cea7cb50492690c20d608cc2367`; the working tree was clean before this audit-fix slice.

- **P2 — Field/event initializer bodies leaked through Core DTOs and Markdown.** A regression with lambda initializers containing block bodies failed before the fix: both `FIELD_BODY_MARKER` and `EVENT_BODY_MARKER` were present in `SkeletonMemberInfo.Signature`. Field and event signatures are now constructed from modifiers, declared type, and variable name without initializer expressions.
- **P2 — Multi-variable declarations shared one signature and only the first symbol handoff.** A regression with two fields and two events failed before the fix: Markdown contained two member entries instead of four. The walker now creates one DTO per declarator and obtains a distinct Roslyn symbol/Handoff ID for each variable. The test follows all four IDs through `FeatureContextScanner` and checks the corresponding symbol names.
- Updated the current-state File Skeleton page and Cluster 4 checklist. The point 4.3 audit checkbox remains `[ ]` pending the requested independent follow-up audit.

### Verification

| Gate | Result |
|---|---|
| Initializer-body leakage regression before fix | Failed as expected: both initializer markers appeared in the DTO signatures |
| Multi-variable handoff regression before fix | Failed as expected: two declarations produced only two Markdown member entries |
| Focused `SkeletonMapTests` after fix | Passed, 14/14 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 291/291 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 303/303 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 4.3

- Reviewed commit: `beeb8992c45feb088948f13d5e8a27a3e4ce8fd5` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- The implementation slice's gate results are recorded separately. This audit did not run a build or tests.
- Nested block namespaces now compose to `Outer.Inner`; dotted namespace syntax is retained through `node.Name.ToString()`. Method, constructor, and property expression bodies are excluded by the local signature builders. Type and ordinary member Markdown handoffs have roundtrip tests; null input cases are covered. These observations do not close the two findings below.

### Open findings

1. **P2 — Field and event initializers can expose executable bodies in a skeleton.** `SkeletonSyntaxWalker.cs:183-188` and `220-225` put `node.ToString()` into the DTO signature, and `SkeletonMarkdownRenderer.cs:101-107` emits that string unchanged. For `private Func<int> compute = () => { return 42; };` or an event initialized with a lambda, the skeleton includes the lambda body and `return 42`, contrary to the point's body-free skeleton requirement. The existing body test (`SkeletonMapTests.cs:74-95`) covers methods, constructors, and a computed property, but no initializer. **Acceptance:** form field/event signatures from declarative syntax without initializer expressions, retain type/modifiers/names, and assert that DTO and Markdown omit executable initializer markers for field and event cases.
2. **P2 — A multi-variable declaration has one handoff that identifies only its first variable.** `SkeletonSyntaxWalker.cs:185-188` and `222-225` select `Variables.FirstOrDefault()` while the signature contains every variable (`int First, Second;` or `event Action First, Second;`). The ID beside that line resolves only to `First`, leaving `Second` without a navigable entry and making the printed association ambiguous. Current handoff tests (`SkeletonMapTests.cs:133-170`) use one type and one single-variable member. **Acceptance:** emit a separate DTO and Markdown entry or another explicit ID-to-name association for each declared variable, and verify that the later field and event variable handoffs roundtrip to their own symbols.

The point 4.3 audit checkbox remains open pending fixes and a follow-up audit. No product code or external repository was changed in this audit.

## Independent audit 2/3 of point 4.3

- Reviewed commit: `855b7eb6abbc696112b98d2ad353cfa351cc35a9` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Read-only comparison: the local walker, builder, renderer, updated FastTests and documentation against the two point 4.3 findings. The implementation slice's gate results are recorded above; this audit did not run a build or tests.

### Finding disposition

1. **P2 initializer-body leakage — closed.** `BuildVariableMemberInfos` constructs field/event signatures from modifiers, type, and each variable declarator without `Initializer`; the renderer receives those DTO signatures unchanged. The new structured-result test asserts absence of both field and event lambda body markers, and the Markdown test asserts absence of those markers in rendered text. Ordinary field/event names and declaration forms remain present.
2. **P2 multi-variable handoff association — implementation fixed, one test obligation open.** `ExtractMembers` now emits one `SkeletonMemberInfo` per field or event declarator and calls `GetDeclaredSymbol(variable)` for each. The Markdown test verifies four distinct `h:...` IDs for two fields and two events and follows each through `FeatureContextScanner` to its own symbol name. **P3 residual test gap:** the structured DTO path is exercised only with one field and one event, without a symbol formatter (`SkeletonMapTests.cs:97-119`); the multi-variable test starts at `FileSkeletonBuilder.BuildMarkdownForDocumentAsync` (`SkeletonMapTests.cs:122-160`). The audit's acceptance also calls for DTO handoff associations for later variables. **Acceptance:** build a multi-variable DTO with an ID formatter and assert separate entries/IDs for later field and event variables, resolving those IDs to the corresponding symbols. Keep the existing Markdown roundtrip test.

The point 4.3 audit checkbox remains open for the residual DTO test obligation. No product code or external repository was changed in this audit.

## Point 4.3 audit 2 test obligation

- Base commit: `907ea87d3052f7939166402a248507aecd68b616`; working tree was clean before this test-only slice.
- **P3 structured DTO handoff coverage — closed by test.** Added a `SkeletonMapBuilder.BuildForDocumentAsync` contract test that supplies `formatSymbolId`, asserts four separate member DTOs for two fields and two events, and compares each formatted DTO ID to the documentation ID of the corresponding Roslyn declarator symbol. This explicitly covers the later field and event variables. The existing Markdown test still roundtrips every emitted opaque ID, including the later variables, through `FeatureContextScanner`.
- No production change was needed: the new contract test passed against the current implementation. Updated the File Skeleton page to state that the ID formatter runs independently for every field/event variable, and marked the multi-variable acceptance item complete in Cluster 4.
- The point 4.3 audit checkbox remains `[ ]` pending the requested final independent audit.

### Verification

| Gate | Result |
|---|---|
| Focused DTO formatter association test | Passed, 1/1 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 292/292 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 304/304 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 3/3 of point 4.3

- Reviewed commit: `36b8d9bd93172763fc184db847b515c567dd7d39` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Narrow read-only audit of the remaining DTO `formatSymbolId` obligation; the implementation slice's gate results are recorded above. This audit did not run a build or tests.
- **P3 DTO handoff test gap — closed.** `BuildForDocumentAsync_FormatsDistinctHandoffsForEachFieldAndEventVariable` supplies a formatter and asserts four separate DTO member IDs matching the Roslyn documentation IDs for `_first`, `_second`, `Changed`, and `Closed`. It also asserts four distinct IDs. The earlier Markdown test still follows all four opaque IDs through `FeatureContextScanner` to their own symbols. Together they cover the DTO association and navigable text roundtrip required by the second audit.

Point 4.3 is accepted after the third and final audit; no open finding or technical debt remains for this point. The audit checkbox is complete. No product code or external repository was changed in this audit.

## Point 4.4 implementation

- Base commit: `75ba20e061dd1b99161c9d5bbe8fe17501d2bca2`; the working tree was clean before this slice.

- **P2 — Record kind and primary-constructor parameters were incomplete.** A regression with a record class and record struct failed before the fix: the record class reported only `Record` and the primary constructor parameters were absent. The scanner now distinguishes `Record Class` from `Record Struct` and emits each positional record parameter as a `PrimaryCtor-Param` entry. The `Constructor` kind filter includes these parameter entries.
- **P2 — Constant fields omitted their literal values.** A regression covering floating-point, negative integer, string, null, character, and boolean constants failed before the fix because the signatures contained only field names and types. Constant signatures now append Roslyn's invariant primitive formatting.
- **P2 — Null inputs leaked a null dereference.** `ScanAsync(null!)` failed before the fix with `NullReferenceException`. Public scan and resolver entry points now validate required solution/identifier inputs, and `RenderMarkdown` validates its payload. A bad `h:` handoff already returned the recoverable `HANDOFF_UNKNOWN` payload before changes; this behavior remains covered. Member handoff roundtrip to `FeatureContextScanner` passed before and after the changes.
- Added the current-state page [Get Class Structure](../../../docs/navigation/get-class-structure.md), indexed it in `docs/README.md`, and expanded Core contract tests for member kinds/visibility, records/interfaces, invariant constants, truncation behavior, handoffs, and errors. The independent 4.4 audit checkbox remains `[ ]`.

### Verification

| Gate | Result |
|---|---|
| Pre-fix record/primary-parameter regression | Failed as expected: `Record` kind and no `PrimaryCtor-Param` entries |
| Pre-fix constant-literal regression | Failed as expected: constant signatures omitted literal values |
| Pre-fix null-request regression | Failed as expected: `NullReferenceException` |
| Focused `ClassStructureScannerTests` after fix | Passed, 12/12 |
| `pwsh -File ./scripts/build.ps1` | Passed, 0 warnings and 0 errors |
| `pwsh -File ./scripts/test-fast.ps1` | Passed, 300/300 |
| `pwsh -File ./scripts/test-integration.ps1` | Passed, 12/12 |
| `pwsh -File ./scripts/test.ps1` | Passed, 312/312 across both test projects |
| `git diff --check` | Passed before commit |

## Independent audit 1/3 of point 4.4

- Reviewed commit: `71d14ba9bcb5faa7e23703778a899f35facba2ab` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- The implementation slice's gates are recorded above. This audit did not run a build or tests.
- Member kinds, declared visibility, record class/struct distinctions, primary constructor rows, invariant constant literals, filter order, truncation counts, opaque member handoffs, and the covered null/unknown-handoff cases agree with the stated Core contract. The outstanding common short-name ambiguity is already point 4.7 and is not counted again here.

### Open findings

1. **P2 — Partial-type Markdown loses the file for each member.** `CreateMemberEntry` retains `FilePath` (`ClassStructureScanner.cs:290-321`), but `RenderMarkdown` (`ClassStructureScanner.cs:450-457`) always prints only kind, name, visibility, lines, signature, and handoff. Two partial declarations can have methods at the same local line number, leaving their Markdown rows indistinguishable by source file. The local tests use single-file types only. **Acceptance:** include each member's declaring file in the Markdown table for multi-file types (or always), preserve DTO file paths, and test partial declarations with members at overlapping line numbers.
2. **P2 — Unescaped member signatures can break the Markdown table.** `RenderMarkdown` interpolates `m.Signature` and `m.Name` directly into pipe-delimited rows (`ClassStructureScanner.cs:453-457`). A valid `operator |` signature or a constant string containing `|` adds an unintended column; a newline in a literal can split the row. **Acceptance:** escape dynamic table cells without losing the signature content or handoff, and assert a stable column count for an operator signature and a constant string containing a pipe (plus line-break handling).

The point 4.4 audit checkbox remains open pending fixes and a follow-up audit. No product code or external repository was changed in this audit.

### Point 4.4 audit finding fixes

- Added a `File` Markdown column for multi-file types, populated from each member's retained `FilePath`; the regression uses two partial declarations whose member rows both start on line 1 and checks the corresponding file name on each row.
- Markdown table cells now escape `|` and normalize CR/LF to spaces. FastTests scan a legal `operator |` and a constant string containing `left|right`, verify the original signature content remains visible, preserve the operator's handoff, and assert the row has exactly the expected table separators. A DTO-level renderer test exercises CRLF values in both the name and signature.

- Before-fix reproductions: both the missing multi-file header and the extra unescaped operator separator failed; the CRLF renderer test failed because a carriage return remained in the row.
- Verification: focused ClassStructureScanner FastTests passed 15/15; `pwsh -File ./scripts/build.ps1` passed with 0 warnings/errors; `pwsh -File ./scripts/test-fast.ps1` passed 303/303; `pwsh -File ./scripts/test-integration.ps1` passed 12/12; `pwsh -File ./scripts/test.ps1` passed 315/315; `git diff --check` passed.
- The point 4.4 audit checkbox remains open for independent follow-up.

## Point 4.5 implementation

- Base commit: `41ca1ac76f666de53596944c457898481c5b523b`; the working tree was clean before this slice.
- Its test-context result explicitly describes static candidates from heuristics and disclaims runtime execution evidence; the Core contract documents the same boundary.
- **P2 — Ordinary identifiers were classified as tests by suffix substring.** Before the fix, `Latest.cs`, `Contest.cs`, type `Latest`, and project `Contest` all matched a case-insensitive `Test` suffix. File, class, and project detection now require a name boundary or an uppercase affix start. Added regressions for all four cases.
- **P2 — Helper libraries alone caused test-project classification.** Before the fix, a production project with only a `Moq.dll` metadata reference was classified as a test project because mocking/assertion packages were in the framework keyword list. Framework detection now uses test-runner/framework references only; the synthetic `Moq.dll` regression failed before and passes after the fix.
- **P2 — MSTest was reported as NUnit.** Before the fix, a three-project repro found xUnit, NUnit, and MSTest fixtures, but `[TestMethod]` was categorized as NUnit because the broad `Test*` check ran first. Framework detection now uses exact recognized attribute names and checks MSTest attributes explicitly; the repro asserts all three framework results across projects.
- **P2 — A name-only fixture was labeled xUnit.** A fixture matched by naming convention but containing no recognized framework attribute previously received an unsupported `xUnit` label. It now reports `Unknown`; a pre-fix regression failed with the old label. Model XML docs and the current-state page identify these results as heuristic candidates, not test execution or coverage evidence.
- **P2 — Null builder arguments leaked a null dereference.** `TestRecommendationBuilder.BuildAsync` now validates the symbol and solution and throws `ArgumentNullException`; a pre-fix regression observed `NullReferenceException` for a null symbol.
- Added [Test Context](../../../docs/navigation/test-context.md) and indexed it in the current-state docs. Point 4.8 remains responsible for the later deeper multi-project/equal-name and recommendation-strength audit.
- Verification: focused `TestDetectorTests` passed 16/16; `pwsh -File ./scripts/build.ps1` passed with 0 warnings/errors; `pwsh -File ./scripts/test-fast.ps1` passed 311/311; `pwsh -File ./scripts/test-integration.ps1` passed 12/12; `pwsh -File ./scripts/test.ps1` passed 323/323; `git diff --check` passed. The independent point 4.5 audit checkbox remains `[ ]`.

## Independent audit 2/3 of point 4.4

- Reviewed commit: `069656674556331552182025af02a4c9d9ba6036` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Narrow read-only review of the two Markdown findings, changed tests, and current-state documentation. The implementation slice's gate results are recorded above; this audit did not run a build or tests.

### Finding disposition

1. **P2 member file in multi-file output — closed.** `RenderMarkdown` adds a `File` column when `p.Files.Count > 1` and uses each member's existing `FilePath`; the single-file table shape stays the same. A new partial-type test places methods on the same source line in two files and checks their separate filenames in the rendered rows.
2. **P2 table escaping — closed.** `EscapeTableCell` replaces CRLF, CR, and LF with spaces and escapes pipe characters for dynamic row values. New tests inspect an `operator |` signature, a constant string containing `|`, the retained handoff, row delimiter counts, and CRLF normalization in a synthetic payload.

No open finding remains for point 4.4. The audit checkbox is complete after two audits. No product code or external repository was changed in this audit.

## Independent audit 1/3 of point 4.5

- Reviewed commit: `6b1d71574985a25b564c907bd7590cd6e91a8310` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- The implementation slice's gate results are recorded above. This audit did not run a build or tests.
- Local tests substantiate xUnit/NUnit/MSTest attribution across separate projects, negative ordinary-name and Moq-only reference cases, `Unknown` for a name-only candidate, null builder arguments, and a basic production-to-test fixture match. Recommendations are explicitly static heuristic candidates. The equal-class-name multi-project loss in `TestRecommendationBuilder` is already the subject of point 4.8 and is left to that point's dedicated implementation and audit.

### Open finding

1. **P2 — Relative root test directories are missed by file classification.** `TestDetector.IsTestFile` (`TestDetector.cs:108-130`) checks suffixes and embedded segments such as `/tests/`, but not a relative path beginning `tests/` or `test/`. Thus `tests/Helpers.cs` and `test/Helpers.cs` return false while `src/tests/Helpers.cs` returns true. The local path test (`TestDetectorTests.cs:17-31`) covers only the embedded form. This can also misclassify source locations passed as relative paths by scope consumers. **Acceptance:** recognize those two relative root prefixes without accepting ordinary filenames like `Contest.cs`, and test `IsTestFile` plus at least one consumer path/scope behavior using a root-relative test file.

The point 4.5 audit checkbox remains open pending the focused fix and follow-up audit. No product code or external repository was changed in this audit.

### Point 4.5 audit finding fix

- Before the fix, direct `IsTestFile("tests/Helpers.cs")` and `IsTestFile("test/Helpers.cs")` regressions both failed, and `FindSymbolScanner` returned no entries for those same root-relative paths in `scopeType=tests`.
- `TestDetector.IsTestFile` now checks anchored `tests/` and `test/` prefixes after path normalization, before existing suffix and embedded-segment checks. The existing ordinary-name negatives remain covered, with an additional `contest/Helpers.cs` case guarding against an unanchored substring rule.
- A consumer test gives two documents exact relative file paths via the Roslyn solution API and verifies both appear in the test scope, while an ordinary `Contest.cs` type remains in production scope and excluded from test scope.

- Verification: focused path test passed 12/12; focused `FindSymbolScanner` scope consumer passed 1/1; `pwsh -File ./scripts/build.ps1` passed with 0 warnings/errors; `pwsh -File ./scripts/test-fast.ps1` passed 315/315; `pwsh -File ./scripts/test-integration.ps1` passed 12/12; `pwsh -File ./scripts/test.ps1` passed 327/327; `git diff --check` passed. The independent point 4.5 audit checkbox remains `[ ]` pending follow-up.

## Independent audit 2/3 of point 4.5

- Reviewed commit: `b3ad5ecb2be8ba10ec3f2f9ff3199f81c4174119` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Narrow read-only review of the relative-path fix, direct classifier tests, FindSymbolScanner scope regression, and documentation. The implementation slice's gate results are recorded above; this audit did not run a build or tests.
- Direct tests cover both prefixes and the negative `contest/Helpers.cs` case. A Roslyn solution test assigns exact root-relative document paths and verifies both declarations enter the `tests` scope while an ordinary `Contest.cs` declaration stays in `production`. No broader name-classification regression was found in the reviewed paths.

No open finding remains for point 4.5. The audit checkbox is complete after two audits. The equal-name multi-project and recommendation-depth work remains explicitly assigned to point 4.8. No product code or external repository was changed in this audit.

## Point 4.6 implementation

- Base commit: `68a9d67fdde4f4317a9220db106b2fac10bf4ba3`; the working tree was clean before this slice.
- The local implementation keeps Linter violations and quality metrics out of the feature-context payload.
- **P2 — Invalid limits were not safely bounded.** Before the fix, `MaxCallers=0` yielded no callers when 60 references existed. The scanner now clamps requested caller and test counts to 1–50, sorts before truncating, and preserves scoped totals/truncation flags. A temporary pre-fix cap repro disabled the test clamp and failed with 55 returned recommendations instead of 50.
- **P2 — Public null/blank inputs leaked runtime failures or lacked a contract.** Before the fix, null Scan requests and null Markdown payloads threw `NullReferenceException`. Public entry points now validate null solutions and blank identifiers, while unresolved ordinary names return `null` and invalid handoffs remain structured navigation errors.
- Added Core contract coverage for declaration, caller and test-method Handoff round-trips, `production`/`tests` caller scopes, 60-call and 55-test truncation, recoverable resolution errors, argument validation, and Markdown null handling. Existing renderer coverage asserts the absence of violations and metrics.
- Added [Get Feature Context](../../../docs/navigation/get-feature-context.md) to the current-state documentation index. Test recommendations remain static heuristic candidates and are not runtime coverage evidence.
- Verification: focused `FeatureContextScannerTests` passed 8/8; `pwsh -File ./scripts/build.ps1` passed with 0 warnings/errors; `pwsh -File ./scripts/test-fast.ps1` passed 321/321; `pwsh -File ./scripts/test-integration.ps1` passed 12/12; `pwsh -File ./scripts/test.ps1` passed 333/333; `git diff --check` passed.
- The independent point 4.6 audit checkbox remains `[ ]` for follow-up.

## Independent audit 1/3 of point 4.6

- Reviewed commit: `6b3e8bf4ede7ed3957e12933f920d3b33b7d33f8` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- The implementation slice's gate results are recorded above. This audit did not run a build or tests.
- The local payload contains declaration/signature, incoming reference locations, static test candidates, bounded and sorted result lists, and structured handoffs/errors. It contains no Linter violations or quality metrics. Existing tests cover ordinary caller and test-method handoff roundtrips, basic path-based caller scopes, caps/truncation, and null/unknown-handoff cases. The shared ambiguous-name resolver remains point 4.7; equal-name test-fixture behavior remains point 4.8.

### Open findings

1. **P2 — Caller scope ignores the declaring project.** `CollectCallersAsync` retains the Roslyn `Document` and `ProjectName` (`FeatureContextScanner.cs:220-257`), but `FilterCallersByScope` (`264-280`) classifies only `TestDetector.IsTestFile(c.FilePath)`. A caller in project `App.Tests` at a neutral path such as `src/TestHost/Shared.cs` enters `production` and is omitted from `tests`. The current scope test (`FeatureContextScannerTests.cs:99-120`) uses a test-named file and project, so it cannot distinguish the two classifiers. **Acceptance:** apply scope using the originating Roslyn document/project before totals and limits, and test a neutral-name test-project caller against `all`, `production`, and `tests` with correct totals.
2. **P2 — The request scope does not filter test recommendations.** `ScanAsync` applies `request.Scope` to callers, then flattens every test candidate from `TestRecommendationBuilder` without scope filtering (`FeatureContextScanner.cs:78-96`). Thus `Scope=Production` can still return test-project entries in `Tests`, with `TotalTests` and truncation calculated over excluded candidates. The local test scope case asserts only `Callers`; the recommendation test uses default `all`. **Acceptance:** scope recommendations by their source document/project before sorting, totals, and `MaxTests`; test `all`, `production`, and `tests` with project-based and path-based test evidence, including a count/truncation assertion after filtering.

The point 4.6 audit checkbox remains open pending fixes and a follow-up audit. No product code or external repository was changed in this audit.

### Point 4.6 audit finding fixes

- **P2 — Caller scope used only the rendered path.** Before the fix, `Scope=production` included a caller from `Sample.App.Tests` in a neutral `src/Shared.cs`, and `Scope=tests` omitted it. `CollectCallersAsync` now applies scope while the Roslyn `Document` is available, using its project classification and document path before accumulating totals or applying the caller cap.
- **P2 — Test recommendations bypassed scope.** Before the fix, `Scope=production` returned two test-project recommendations and reported `TotalTests=2`. Each fixture now retains its originating Roslyn `ProjectId` internally; feature-context recommendations are filtered by that project and source path before sorting, totals, and `MaxTests`.
- FastTests cover caller and recommendation scoping for `all`, `production`, and `tests`, using a neutral document in a test-named project and a root-relative `tests/` document in a normally named project. The recommendation assertions verify scoped totals and truncation after filtering.
- Updated [Get Feature Context](../../../docs/navigation/get-feature-context.md) to describe project/document-based scope and its ordering before limits.
- Verification: focused scope regressions and `FeatureContextScannerTests` passed; `pwsh -File ./scripts/build.ps1` passed with 0 warnings/errors; `pwsh -File ./scripts/test-fast.ps1` passed 323/323; `pwsh -File ./scripts/test-integration.ps1` passed 12/12; `pwsh -File ./scripts/test.ps1` passed 335/335; `git diff --check` passed.
- The independent point 4.6 audit checkbox remains `[ ]` for follow-up.

## Independent audit 2/3 of point 4.6

- Reviewed commit: `bfc92d50606c63d0ae1d45eb1b78cf1808f2060d` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Narrow read-only review of the two scope fixes, their tests, and current-state documentation. The implementation slice's gate results are recorded above; this audit did not run a build or tests.

### Finding disposition

1. **P2 caller project scope — closed.** `CollectCallersAsync` now applies `MatchesScope(IsTestDocument(doc, relPath), scope)` while the Roslyn document is available, before accumulating, sorting, totals, and limits. `IsTestDocument` considers project metadata/name and document/relative paths. The new test separates a neutral-file test-project caller, a path-based test caller, and a production caller, checking `all`, `production`, and `tests` counts and membership.
2. **P2 recommendation scope — closed.** `TestFixtureMatch` internally retains the source `ProjectId`; `FlattenTestRecommendations` checks both source project and file path before sorting, totals, truncation, and `MaxTests`. The new test supplies project-based and path-based candidates and verifies `Scope=production` returns zero tests without truncation, while `all` and `tests` report the scoped totals and one-item truncation.

No open finding remains for point 4.6. The audit checkbox is complete after two audits. Common symbol resolution and equal-name fixture handling remain assigned to points 4.7 and 4.8. No product code or external repository was changed in this audit.

## Point 4.7 implementation

- Base commit: `ca6a88c` (clean working tree before this slice).

- **P2 — Follow-up tools did not share symbol identifier semantics.** Before the fix, `M:Sample.Core.Greeter.Greet` and a source `file:line:column` identifier resolved to `null` in Feature Context; the short name `Run` selected the first matching declaration rather than returning `AMBIGUOUS_SYMBOL`. Feature Context also had no typed candidate list. These tests were added and observed red before the resolver integration.
- Added Core `SourceSymbolResolver.ResolveAsync` as the common path for Body, Feature Context, and Class Structure. It recognizes `h:`/`i:` handoffs, Roslyn documentation IDs, exact simple and qualified names (including method signatures), and absolute/solution-relative `path:line:column` or `path:line` locations. Position validation and unknown documents return structured errors. Multiple matches return `AMBIGUOUS_SYMBOL` with candidate name, kind, signature, relative path, declaration lines, project, documentation ID, and selectable `h:` handoff.
- Body resolution now has an identifier-based result that preserves error and candidate choices. Feature Context and Class Structure expose candidates on error payloads. Tests exercise ambiguous candidate selection and roundtrips from `find_symbol` handoff, documentation ID, column position, and line-only position through all three downstream scanners. Class Structure separately covers a documentation ID, position, and same-qualified-name declarations in distinct projects. A prior path-classification test now asserts the structured `SYMBOL_NOT_FOUND` contract for ordinary Windows drive paths.
- Added [Shared Symbol Resolution](../../../docs/navigation/symbol-resolution.md) and linked/updated the Body, Feature Context, Class Structure pages and docs index.
- Verification: focused Feature Context scanner tests passed 15/15 before the last test additions; Class Structure passed 16/16; vertical `find_symbol` identifier roundtrip passed 1/1. The first full FastTests pass surfaced an old expectation that an unknown Windows path returns `null`; the test now checks the explicit `SYMBOL_NOT_FOUND` payload. Final `pwsh -File ./scripts/build.ps1` passed with 0 warnings/errors; `pwsh -File ./scripts/test-fast.ps1` passed 329/329; `pwsh -File ./scripts/test-integration.ps1` passed 12/12; `pwsh -File ./scripts/test.ps1` passed 341/341; `git diff --check` passed.
- The 4.7 implementation items are checked in `Clusters/Cluster-04.md`; the independent 4.7 audit checkbox remains `[ ]` for follow-up. Point 4.8 is unchanged.

## Independent audit 1/3 of point 4.7

- Reviewed commit: `b83cfd9ef2460550252f1aee5a53b49851ef34f9` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Local review followed `SourceSymbolResolver` into Body, Feature Context, and Class Structure, their DTOs, tests, and the current-state page. The implementation slice's gates are recorded above; this audit did not run a build or tests.
- The common Core route, project-marked `h:` handoffs, `i:` validation, documentation IDs, source positions, candidate metadata, and three-consumer handoff roundtrips are present. Ambiguous same-name declarations in separate projects are tested. The findings below prevent closing the point.

### Open findings

1. **P2 — Metadata-only types escape the documented source boundary through the name fallback.** `SourceSymbolResolver.ResolveByNameAsync` (`SourceSymbolResolver.cs:213-224`) returns the first `compilation.GetTypeByMetadataName(normalized)` result without requiring a declaration in this solution. For example, `System.String` can succeed as a metadata symbol even though documentation IDs are source-filtered and `docs/navigation/symbol-resolution.md` explicitly promises no metadata-only name result. The success has no source candidate or selectable handoff, and the three consumers can receive a metadata type. **Acceptance:** return `SYMBOL_NOT_FOUND` for a metadata-only name in this source resolver (or explicitly revise the source contract with a separate supported metadata path), retain source type resolution, and test the result in Body, Feature Context, and Class Structure against a framework type.
2. **P2 — A cursor on a token with no symbol silently selects an enclosing declaration.** `ResolveSymbolAtToken` (`SourceSymbolResolver.cs:394-405`) walks every ancestor until it finds a declaration or reference. Thus a column on punctuation or a literal inside a method can resolve the containing method; line-only resolution can add that method as a spurious candidate alongside actual declarations or references. Existing local position tests place the cursor directly on a declaration name. **Acceptance:** limit position lookup to the token's semantic symbol rather than an arbitrary enclosing declaration, preserve declaration/reference and property-accessor behavior, and test both column and line-only forms with a symbol-free token in a method body.

The point 4.7 audit checkbox remains open pending a focused fix and follow-up audit. No product code or external repository was changed in this audit.

### Point 4.7 audit finding fixes

- **P2 metadata fallback — closed.** Removed the `GetTypeByMetadataName` name fallback. A regression checks that `System.String` is rejected as `SYMBOL_NOT_FOUND` by Body, Feature Context, and Class Structure, while earlier source type resolution tests remain in place.
- **P2 symbol-free positions — closed.** Position lookup now checks only declaration-name tokens, accessor keywords, and identifier references with semantic symbols. Added column and line-only regressions for a string literal, plus a column regression for a method-body closing brace. These return `SYMBOL_NOT_FOUND` instead of the enclosing method.
- Verification: focused source-boundary regression passed 1/1; `FeatureContextScannerTests` passed 16/16; `pwsh -File ./scripts/build.ps1` passed with 0 warnings/errors; `pwsh -File ./scripts/test-fast.ps1` passed 330/330; `pwsh -File ./scripts/test-integration.ps1` passed 12/12; `pwsh -File ./scripts/test.ps1` passed 342/342; `git diff --check` passed.
- The 4.7 audit checkbox remains `[ ]` pending a follow-up audit. Point 4.8 is unchanged.

## Independent audit 2/3 of point 4.7

- Reviewed commit: `b832e613ef432b6d14306ccc3410a31d8e500704` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Narrow read-only review of the two 4.7 corrections, their regression test, the source resolver's consumer calls, and current-state documentation. The implementation slice's gate results are recorded above; this audit did not run a build or tests.

### Finding disposition

1. **P2 metadata-only name fallback — closed.** `ResolveByNameAsync` now returns only the results of `SymbolFinder.FindSourceDeclarationsAsync`; the `GetTypeByMetadataName` fallback is gone. The regression checks `System.String` returns `SYMBOL_NOT_FOUND` through direct resolution and each Body, Feature Context, and Class Structure consumer. Existing source type and member roundtrip tests remain.
2. **P2 enclosing-declaration position fallback — closed.** `ResolveSymbolAtToken` now accepts declaration-name tokens, accessor keywords, and identifier references only. A literal and method-body closing brace return no symbol; line-only lookup over the literal-only return line likewise reports `SYMBOL_NOT_FOUND`. The existing declaration-name, documentation ID, `h:` handoff, and line-only declaration roundtrips remain covered. The change prevents an ancestor method declaration from being selected for symbol-free tokens.

No open finding remains for point 4.7. The audit checkbox is complete after two audits. No product code or external repository was changed in this audit.

## Point 4.8 implementation

- Base commit: `5a992c7`; the working tree was clean before this slice.

- **P2 — Same-named fixtures were deduplicated across projects.** Before the fix, a two-project reproduction with `OrderServiceTests` in both projects returned one fixture (`Expected: 2`, `Actual: 1`). `BuildAsync` searched the whole solution once per project/candidate and skipped any result whose class name had already appeared.
- `BuildAsync` now searches candidate declarations once across the solution and deduplicates by Roslyn symbol identity, preserving distinct symbols from different projects. Candidate results are deterministically ordered and expose `ProjectName`, source path, and project-bound handoff IDs so equal class names can be distinguished and selected.
- The existing three-project xUnit/NUnit/MSTest regression now also verifies that MSTest `TestMethodAttribute` creates a method entry with a reusable handoff. The pre-existing 4.5 fix already classifies this exact attribute as MSTest; this slice strengthens the contract test rather than changing that already-correct behavior.
- Kept name-only matches at `Unknown` and documented in the public builder XML contract and [Test Context](../../../docs/navigation/test-context.md) that recommendations are static heuristic candidates, not evidence of test execution or coverage.
- Verification: the equal-name pre-fix regression failed with 1 result instead of 2; the focused `TestRecommendationBuilder` suite passed 5/5 after the fix. `pwsh -File ./scripts/build.ps1` passed with 0 warnings/errors; `pwsh -File ./scripts/test-fast.ps1` passed 331/331; `pwsh -File ./scripts/test-integration.ps1` passed 12/12; `pwsh -File ./scripts/test.ps1` passed 343/343; `git diff --check` passed.
- The independent point 4.8 audit checkbox remains `[ ]`; no 4.9 work is included.

## Independent audit 1/3 of point 4.8

- Reviewed commit: `4969b1a7e8119961530240461380fadf3abe3536` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- The implementation slice's gates are recorded above; this audit did not run a build or tests.
- The builder now searches once across the solution and deduplicates by Roslyn symbol identity, preserving two equal-named fixture symbols from different projects. The regression asserts separate paths, project names, frameworks, methods, and distinct `h:` values. The xUnit/NUnit/MSTest fixture test checks attributed methods, including MSTest `TestMethodAttribute` and its method handoff. `Unknown` remains the framework for a name-only candidate. The local tests do not roundtrip the two equal-name project handoffs, but the common project-marked resolver has separate coverage under point 4.7; no failure was established here.

### Open finding

1. **P2 — The static heuristic boundary is absent from the emitted result.** `TestContextPayload` (`TestContextModels.cs:28-36`) has only a source comment, not a data field declaring the evidence mode. The actual Feature Context output renders the candidates under `Associated Tests` (`FeatureContextScanner.cs:340-350`) without a heuristic or non-coverage qualifier; its DTO likewise has no evidence marker. A consumer of the result can therefore read name/project matches as established associated tests even though the current-state page correctly calls them static heuristic candidates. **Acceptance:** expose an explicit static-candidate/evidence-mode marker in the machine-readable result and label the rendered test section as heuristic; test both a name-only `Unknown` candidate and an attributed candidate to ensure neither is presented as execution or coverage evidence.

The point 4.8 audit checkbox remains open pending a focused fix and follow-up audit. No product code or external repository was changed in this audit.

### Point 4.8 audit finding fix

- Added a read-only `TestContextPayload.EvidenceMode` property with the wire value `static-test-candidates-only` and a named constant. Core contract tests assert the value on the name-only `Unknown` heuristic candidate.
- Feature Context Markdown labels its `Associated Tests` section as static heuristic candidates, prints `static-test-candidates-only`, and states that test execution and coverage are not verified. The renderer test includes both an attributed xUnit candidate and a name-only `Unknown` fixture and checks that the disclaimer is visible alongside both results.
- Before the fix, the DTO contract repro failed because `EvidenceMode` was absent, and the renderer repro failed because neither the evidence marker nor the execution/coverage disclaimer appeared.
- Verification: focused DTO and Markdown contracts passed 1/1 each; `pwsh -File ./scripts/build.ps1` passed with 0 warnings/errors; `pwsh -File ./scripts/test-fast.ps1` passed 331/331; `pwsh -File ./scripts/test-integration.ps1` passed 12/12; `pwsh -File ./scripts/test.ps1` passed 343/343; `git diff --check` passed.
- The 4.8 audit checkbox remains `[ ]` pending follow-up; no 4.9 work is included.

## Independent audit 2/3 of point 4.8

- Reviewed commit: `5d812faaf8da3870e5e27d2b148032b6b06ad904` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Narrow review of the evidence-mode DTO change, Feature Context Markdown, its regressions, and downstream model shape. The implementation slice's gate results are recorded above; this audit did not run a build or tests.

### Finding disposition

1. **Test-context evidence mode and rendered disclosure — closed.** `TestContextPayload.EvidenceMode` is a public read-only `static-test-candidates-only` value. The Markdown heading and preceding line now describe static heuristic candidates and explicitly disclaim verified execution or coverage. The renderer regression includes both an attributed xUnit method and a name-only `Unknown` fixture, and the DTO regression checks the wire value.
2. **P2 structured Feature Context result — remains open.** `FeatureContextScanner.ScanAsync` flattens those same candidates into `FeatureContextPayload.Tests`, but `FeatureContextModels.cs:28-48` still has no evidence-mode field on either the test entries or their containing payload. Consumers reading the structured result instead of `RenderMarkdown` cannot see the new disclosure; this is the remaining DTO portion of the first audit finding. **Acceptance:** carry `static-test-candidates-only` on the Feature Context structured result as well, and assert it for both attributed and name-only candidates without changing the scoped totals or handoff behavior.

The point 4.8 audit checkbox remains open for the remaining structured-result contract and one final audit. No product code or external repository was changed in this audit.

### Point 4.8 structured Feature Context audit finding fix

- Added `FeatureContextPayload.EvidenceMode`, initialized to the same public `static-test-candidates-only` value used by `TestContextPayload`, so structured `ScanAsync` consumers receive the static-evidence boundary.
- Extended the structured scan contract test with an attributed xUnit method and a name-only `Unknown` fixture. It asserts the evidence mode, both candidates and stable total/count, then resolves both candidate handoffs through `SourceSymbolBodyResolver` to verify the method and fixture remain selectable.
- Before the fix, the focused regression failed because the `FeatureContextPayload.EvidenceMode` property was absent. The red test also confirms both candidates and handoffs were already present.
- Updated [Get Feature Context](../../../docs/navigation/get-feature-context.md) to document the structured payload field. No markdown behavior or scope/totals behavior changed.
- Point 4.8 remains open for the final audit; no 4.9 work is included.

## Independent audit 3/3 of point 4.8

- Reviewed commit: `9b479eaa1d6e202cc9e38b3205153835649d9692` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Final narrow read-only review of the structured Feature Context evidence marker and its regression. This audit did not run a build or tests and does not claim separate gate results.
- **P2 structured evidence mode — closed.** `FeatureContextPayload.EvidenceMode` now uses `TestContextPayload.StaticTestCandidatesOnlyEvidenceMode`, so structured consumers see the same `static-test-candidates-only` value as the Test Context DTO and rendered Feature Context. The expanded test asserts the mode with one attributed xUnit method and one name-only `Unknown` fixture, `TotalTests == Tests.Count == 2`, and successful Body roundtrips for both unchanged handoffs. The change adds only a payload property; the scanner's sorting, scope, and count paths are unchanged.

No open finding remains for point 4.8 after the third audit. The audit checkbox is complete. No product code or external repository was changed in this audit.

## Cluster 4 integration review 1 — fix round 0

- Reviewed commit: `4cc9f4d1f506112c638713c3b36f1d7390c3b64f` (clean working tree before review).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`. This is the first cluster integration review, before any cluster-level fix round; it does not reopen or increment the completed point-audit counts.
- Inspected the Core handoff chain from `FindSymbolScanner` and Skeleton Markdown through the common `SourceSymbolResolver` to Body, Feature Context, and Class Structure. The existing vertical tests cover `find_symbol` handoffs, Skeleton type/member and multi-variable handoffs, ambiguous candidate selection, and Body/Feature/Class Structure follow-up. Project-marked source IDs and snapshot validation are shared at the resolver boundary.
- Inspected the interaction of `production`/`tests` scope and generated-source opt-in in symbol search, project/document scope applied to Feature Context callers and test candidates before totals and limits, partial-body and class-structure locations, and the static `EvidenceMode` boundary in Test Context and Feature Context. The point audits and their fixes account for the interface mismatches found in these paths. The Core's source-focused follow-up APIs do not expose a generated-source toggle; the search opt-in controls which declarations are offered, while a selected handoff remains resolvable.
- **Finding disposition:** no new relevant cluster-wide defect established. No cluster-level fix round is required. The point reviews remain the evidence for their local acceptance criteria; the assembly consumer dependency recorded in `Findings.md` belongs to Cluster 3/7, outside this source cluster.
- **Gates:** this documentation-only integration review did not run a build or tests. The most recent full gate result recorded for Cluster 4 is the point 4.8 evidence-mode implementation: build with 0 warnings/errors, FastTests 331/331, integration 12/12, and total 343/343. The final 4.8 structured-payload change has no separate gate result recorded in this review log; its code and focused regression were inspected read-only here. `git diff --check` for this review passed.

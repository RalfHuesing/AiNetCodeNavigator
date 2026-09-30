# Cluster 4 Review Log

## Point 4.1 implementation

- Base commit: `d821f8bc89e60c3bd0b1d75e4ac7eddf60a9752a`; the working tree was clean before this slice.
- Compared `FindSymbolScanner` and its FastTests in AiNetCodeNavigator with AiNetLinter's `FindSymbolScanner`, `McpScopeClassifier`, and classifier tests through read-only AiNetLinter MCP navigation. Name and wildcard matching and `SymbolKind` filtering were present. AiNetCodeNavigator filtered scope using `TestDetector.IsTestSymbol`, while the reference classifies each source location from its project and document.
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
- Read-only AiNetLinter MCP inspection covered `SymbolKindClassifier`, `FindSymbolScanner`, `McpScopeClassifier`, `GeneratedCodeDetector`, and the scope classifier tests. The reference separates delegates and record classes/structs from plain classes/structs and filters generated source per location, with generated inclusion disabled by default.
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
- Read-only comparison: AiNetLinter `FindSymbolScanner`, `SymbolKindClassifier`, `McpScopeClassifier`, and their relevant FastTests. The project/document scope fix and the existing exact name, wildcard, class/method, multi-project, and truncation tests support the checked implementation items. This review did not run a build or tests; the implementation gates above belong to the earlier slice.

### Open findings

1. **P2 — The kind filter does not cover the reference's public vocabulary.** `FindSymbolModels.cs:17-29` has no `delegate`, `record class`, or `record struct` values. `FindSymbolScanner.cs:222-236` also admits record structs for plain `struct`, while the reference `SymbolKindClassifier.MatchesTypeKind` excludes records from plain `class`/`struct` and recognizes the specific record kinds and delegates. The local kind test (`FindSymbolScannerTests.cs:262-275`) covers only class versus method. **Acceptance:** expose and correctly filter all reference kind values, preserve distinct record class/struct results, and test positive and mismatched cases, including a record struct excluded by plain `struct`.
2. **P2 — Generated declarations are returned by default with no opt-in control.** `FindSymbolScanRequest` (`FindSymbolModels.cs:49-56`) has no `IncludeGenerated` option, and `FindSymbolScanner.CollectVisibleLocations` (`FindSymbolScanner.cs:158-185`) admits every source location that passes the project scope. The reference `TryCreateVisibleLocationAsync` excludes `McpSourceKind.Generated` unless `IncludeGenerated` is true. The local tests have no generated-file or generated-header case. **Acceptance:** classify generated locations using the reference conventions, exclude them by default for `all`, `production`, and `tests`, allow explicit inclusion, and cover both generated path/header and a declaration with generated and editable locations.

The point 4.1 audit checkbox remains open pending fixes and a follow-up audit. No product code or external repository was changed in this audit.

## Independent audit 2/3 of point 4.1

- Reviewed commit: `ce047c598178beb5c0824faa668523fe88af6bae` (clean working tree before audit).
- Reviewer: `gpt-6-sol`, reasoning effort `medium`.
- Read-only comparison: AiNetLinter `SymbolKindClassifier.MatchesTypeKind`, `FindSymbolScanner.TryCreateVisibleLocationAsync`, `McpScopeClassifier.ClassifySourceKindAsync`, and `GeneratedCodeDetector.IsGenerated`. The second implementation slice's gate results are recorded above; this audit did not run a build or tests.

### Finding disposition

1. **P2 kind vocabulary — closed.** `FindSymbolModels.cs` now includes delegate and both specific record forms. `FindSymbolScanner.FilterByKind` distinguishes plain class/struct from record class/struct, while generic record includes both. `DescribeKind` returns the specific record form. New FastTests check delegate, generic and specific record results, mismatches, and record struct exclusion from plain struct. The plain class branch also explicitly excludes records. No kind-filter regression was found in the reviewed paths.
2. **P2 generated-source filtering — closed.** `FindSymbolScanRequest.IncludeGenerated` defaults to `false`. `CollectVisibleLocationsAsync` filters each source location after project/document scope classification. `IsGeneratedDocumentAsync` matches the reference's path suffixes, `obj/` segment, first-five-line header, and declared-symbol attribute checks. New FastTests cover generated path variants, header, attribute, explicit inclusion in production and test scopes, and an editable/generated partial declaration. The existing scope and multi-project tests remain present.

No open finding remains for point 4.1 on this reviewed Core implementation. The audit checkbox is complete after two audits. The end-to-end MCP contract remains subject to Cluster 11 verification.

## Point 4.2 implementation

- Base commit: `d7f6e52facf480573bb0db4390dba618ce5b2537`; the working tree was clean before this slice.
- Read-only AiNetLinter MCP inspection covered `SourceSymbolBodyResolver` and `GetSymbolBodyToolTests`. The resolver uses AST declaration text with a one-based `startLine`/`maxBodyLines` window; its tests cover truncation and out-of-range windows. The existing Core code had the same basic approach but did not cover partial-method implementations, executable default interface members, null inputs, source-less metadata symbols, or mixed availability in a batch.
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
- Read-only comparison: AiNetLinter `SourceSymbolBodyResolver` and its AST extraction, availability, and window behavior; inspected the local Core resolver, models, FastTests, and current-state documentation. The implementation slice's gate results are recorded separately. This audit did not run a build or tests.

### Findings and acceptance

- **No open finding for point 4.2.** `GetBodySyntaxReference` selects a partial method implementation when available, and `HasUnavailableBody` distinguishes a bodyless partial definition or abstract interface member from an executable default interface method. `Extract` uses the same one-based, normalized line window and continuation behavior as the reference. `ResolveBatch` projects the input in order and keeps metadata-only symbols as unavailable results; an empty input returns an empty list. FastTests cover those cases, ordinary and abstract members, null direct inputs, and out-of-range windows. The reviewed Core contract meets the listed point 4.2 acceptance criteria.
- The future MCP tool contract, identifier resolution, and real transport remain subject to their later roadmap points and Cluster 11 end-to-end verification.

The point 4.2 audit checkbox is complete after one audit. No product code or external repository was changed in this audit.

## Point 4.3 implementation

- Base commit: `0477f10b6af936ecea10070ad0f3defc37775421`; the working tree was clean before this slice.
- Read-only AiNetLinter MCP inspection covered its `SkeletonSyntaxWalker`, `SkeletonMapBuilder`, `SkeletonMarkdownRenderer`, and walker tests. The reference walks declarations from Roslyn, deliberately ignores nested types, excludes executable method metadata from the rendered signature, and attaches handoff IDs to type/member entries.
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
- Read-only AiNetLinter MCP comparison covered the reference field/event extraction and skeleton tests. The reference emits a whole declaration and assigns one handoff to its first declarator; the audit findings identify two requirements that need stronger behavior in this repository.
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
- Read-only comparison: AiNetLinter `SkeletonSyntaxWalker` and `SkeletonMapBuilder`; inspected the local walker, builder, Markdown renderer, handoff formatter, DTOs, FastTests, and current-state page. The implementation slice's gate results are recorded separately. This audit did not run a build or tests.
- Nested block namespaces now compose to `Outer.Inner`; dotted namespace syntax is retained through `node.Name.ToString()`. Method, constructor, and property expression bodies are excluded by the local signature builders. Type and ordinary member Markdown handoffs have roundtrip tests; null input cases are covered. These observations do not close the two findings below.

### Open findings

1. **P2 — Field and event initializers can expose executable bodies in a skeleton.** `SkeletonSyntaxWalker.cs:183-188` and `220-225` put `node.ToString()` into the DTO signature, and `SkeletonMarkdownRenderer.cs:101-107` emits that string unchanged. For `private Func<int> compute = () => { return 42; };` or an event initialized with a lambda, the skeleton includes the lambda body and `return 42`, contrary to the point's body-free skeleton requirement. AiNetLinter uses the same source-copy pattern, so this inherited behavior requires an adaptation rather than literal reuse. The existing body test (`SkeletonMapTests.cs:74-95`) covers methods, constructors, and a computed property, but no initializer. **Acceptance:** form field/event signatures from declarative syntax without initializer expressions, retain type/modifiers/names, and assert that DTO and Markdown omit executable initializer markers for field and event cases.
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

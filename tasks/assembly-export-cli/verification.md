# Assembly export CLI verification

## Scope and baseline

- Baseline: `716ad3ff1ac3f8fcb59d3807f942d464f666390a`; working tree initially clean.
- Implementation and tests delegated to `gpt-6.1-sol` subagents with medium reasoning; independent final audit used the same requested configuration.
- Changed production files are confined to `src/AiNetCodeNavigator.AssemblyExport/`. The MCP server, Core and `Directory.Packages.props` have no diff.
- The exporter now references the existing centrally pinned `System.CommandLine` package. Selection and reference resolution behavior remain exporter-owned.

## Executed checks

All final checks below passed on 2026-10-08:

| Check | Result |
| --- | --- |
| `pwsh -File ./scripts/build.ps1` | Solution build; zero errors and warnings. Repeated after test corrections. |
| `pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~ExportCommandLineTests\|FullyQualifiedName~ExportPlanningTests\|FullyQualifiedName~ExportDumpOwnershipTests\|FullyQualifiedName~ExportRunnerTests'` | 85 passed, zero failed/skipped. |
| `pwsh -File ./scripts/test-integration.ps1 -Filter 'FullyQualifiedName~AssemblyExportExecutableTests'` | 7 passed, zero failed/skipped. |
| `pwsh -File ./scripts/export-assembly-smoke.ps1 -SkipBuild` | Real generated managed probe exported with complete state; include/exclude selection, navigation artifacts, class maps and dry-run file hash preservation passed. Checked `last-run.log` and the child manifest. |
| Built exporter `--help` | Exit 0; named options shown. |
| `pwsh -File ./scripts/test.ps1` | 967 FastTests and 143 IntegrationTests passed, zero failed/skipped. Integration suite completed in 4m35s; no stall threshold reached. |
| `git diff --check` | Passed. |

The initial focused runs exposed two old positional-pattern test inputs and one overly specific assertion expecting a fully qualified name in decompiled source. The test inputs were migrated; the source assertion now checks the emitted namespace import, type and field declaration, with stronger resolved-reference metadata assertions. The affected selections were rerun successfully before the full routine gate.

Coverage includes global include OR semantics over multiple recursive directories, explicit-file include bypass, case-insensitive `*`/`?` exclusions, exclusion precedence for explicit and dependency files, stopping traversal through an excluded dependency, disabled dependency exports with retained decompilation references, malformed/unknown/duplicate arguments, legacy positional rejection, absent-output dry-run and byte-identical existing-dump preservation for dry-run and empty/invalid selection.

## Independent audit

No open actionable findings. The auditor checked parser, export queue, reference metadata, no-write paths, tests, package binding and consumer documentation. Independent dry-run probes confirmed exit 2 for repeated single-value options, unknown options, invalid Boolean values and additional positional arguments, and exit 0 for a valid dependency-disabled preview. Two stale documentation references to positional arguments were corrected and rechecked.

The auditor independently confirmed no MCP/Core/central-package changes and a clean whitespace check. The full routine gate passed after the audit's code review. ExtendedIntegration, E2EIntegration and performance suites were not run; this task changes no shared navigation, workspace or host dependencies that require a targeted extended gate. The routine FastTests audit launcher is asynchronous and is not counted as independent completed analysis.

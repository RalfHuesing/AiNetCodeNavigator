# Small practical evaluation

Status: proposed, except the response-budget observation below. No production implementation was changed for this review.

## Already observed: response granularity

Same selected type, `get_context`, `sections=[members]`, `maxResults=3`:

| Response budget | Outer calls | Visible UTF-8 text bytes in total | Delivery |
| --- | ---: | ---: | --- |
| 4,096 bytes | 1 | 2,435 | Three members and a domain cursor for the remaining members. |
| 1,024 bytes | 4 | 3,299 | The same three members over JSON fragments; a domain cursor remains. |

Byte counts were calculated with .NET UTF-8 encoding over each returned text block. This is not token accounting, latency measurement or billed usage. The first small page contained the declaration/section envelope but no member item. The response correctly distinguished analysis completion from remaining delivery; the agent still had to follow outer pages before the domain cursor.

Raw [single response](observed-context-response.json) and [small pages](observed-paged-context.json) preserve requests and outputs. Historical cursors are not instructions to resume them. This uses the connected server without verified initialization/build identity, so it is a usability observation only. Checked-in code/docs independently substantiate the paging design. No cold-cache claim is made.

## First decision experiment: completed tasks, not isolated queries

Start with six tasks, with expected evidence prepared before the agent runs:

| Task | Required evidence / trap |
| --- | --- |
| Find and explain an overloaded cross-project API | Correct owner/overload, implementation, one real caller; same-name decoy excluded. |
| Prepare an interface change | Implementations/overrides, direct uses, candidate tests and explicit external/runtime gaps. No edit needed. |
| Investigate a replaced/missing referenced DLL | Exact owner/image and incomplete reference evidence; no stale-source conclusion. |
| Explain different APIs under net48/net10 | Correct loaded context and alternate-context limitation, conditional declarations/reference differences. |
| Find a Blazor event/component connection | Original `.razor` evidence and code/generated link; no claim that C# code-behind alone covers markup. |
| Find a WPF command/binding connection | Explicit XAML/code evidence; runtime DataContext candidate distinguished from proven reference. |

Use this repository for the first two and a controlled small DLL fixture for the third. Use representative permitted solutions or later approved fixtures for the last three. Creating product fixtures/tests is a later implementation task; this review does not imply it was done. First run tasks 1–2 as a low-cost pilot; stop if scoring or expected evidence is ambiguous.

Conditions:

- A: agent with file reads, `rg` and normal environment tools.
- B: same agent and tools plus current Navigator.
- C, only after B is understood: same setup plus a manual/client recipe for bounded change context or improved paging. Do not build a new engine to run this condition.
- Optional D: existing IDE/LSP/Serena setup, only if setup is cheap and it can answer the source-backend product decision.

Use the same model/version, reasoning setting, task text, source revision, effective build context and response requirements. Start separate contexts to prevent answer leakage; alternate task/condition order. Two runs per condition are a pilot, not statistical proof. Separate cold target runs from warm runs; a primed external server is not a cold measurement. Record server informational version and compare it with intended HEAD.

Score independently:

1. Required facts recovered, with exact source/owner evidence; decisive facts missed; false claims (especially false absence/completeness).
2. Answer/plan quality and whether the evidence permits the intended next step. Test candidate discovery alone earns no coverage claim.
3. Agent-visible tool calls, actual transport calls, polling/pages/recovery calls, time to first useful evidence and total wall time. These are separate counts.
4. Total input/output model usage when available, schemas and repeated context, plus captured visible text as a secondary proxy. Never equate `cl100k_base` response counts with the model's billed total.
5. Peak process working set/private bytes, cold load, warm refresh, scan counts and reloads where observable; separate metrics from multiple server processes.

Provisional decision rule, to agree before the larger run: a candidate must introduce no missed decisive facts or false completeness claims in the pilot. Prefer it if it then improves paired median time or total model tokens by at least 20%, without a material regression in the other measure. Twenty percent is a proposed economic threshold, not a result or scientific guarantee. Report task-by-task regressions and tails; expand the pilot before a costly architecture decision.

## Two narrow follow-ups if the pilot supports investment

**Environment feasibility:** A single multi-target fixture with a conditional API and project reference, plus one Razor and one XAML link, is sufficient to expose coverage boundaries. Verify generated/regular documents, context ambiguity, navigation to original source and the loaded SDK. For net48 also include an old-style project; an SDK-style net48 fixture alone cannot validate legacy loading.

**Refresh/resource feasibility:** On a realistic large solution, run repeated focused discovery/body/use queries, then edit source preserving its timestamp, change an import, add/remove a file, and replace a referenced DLL. Record separate refresh/identity/analysis durations and memory. An optimization must preserve the existing same-timestamp and snapshot contract cases. If a watcher is proposed, test overflow/restart and missed-event fallback before claiming freshness.

Existing [manual exploration](../../../docs/development/mcp-exploration.md) and [traffic capture](../../../docs/mcp-traffic-capture.md) are useful collection infrastructure. The exploration runner automatically handles some delivery control; count its internal attempts as transport calls rather than reporting only one scenario call. It does not replace an actual client/agent workflow.

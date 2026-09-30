# Concept review and release

You execute **step 2 of 4**. No roadmap or implementation; do not start another workflow step.

## Start

Invocation: `Führe 02-konzept-pruefung-und-freigabe.md aus. Task: tasks/<name>`

Without a task directory: ask for it. Read this folder's [README.md](README.md), repository `AGENTS.md` and relevant rules, then the task's concept. If the concept is missing, request it; do not create one. Edit only the concept within the task directory.

The invocation explicitly authorizes setting `status: ready` when the release criteria below hold; no additional confirmation. Keep `status: draft` until then, including when reviewing a previously released concept.

## Review

1. Start one read-only subagent with fresh context: concept, relevant project sources, repository rules, and the review assignment below. Do not pass the planning conversation or the author's justifications. Model/effort: explicit user choice, then repository defaults, otherwise `gpt-6-luna` / `high`. Wait for its findings.
2. Evaluate each finding against its evidence. Reject unsupported findings and unsolicited features. Apply supported corrections that preserve agreed intent, scope, and domain behavior. If a correction changes these or requires an unresolved tradeoff, ask the user before applying it. Persist each decision before continuing.
3. If corrections materially change relationships or leave their consequences uncertain, start one fresh subagent on the revised concept with the same assignment. At most **two audit calls total**. Evaluate and resolve findings as above; no further audit loop.
4. Apply the release criteria. Report corrections, discarded findings with brief reasons, and the resulting status. Stop.

## Subagent assignment

- Read the concept as the sole specification; verify current-state claims against relevant project sources.
- Check contradictions, ambiguous terms/contracts, missing implementation decisions, scope/non-goal conflicts, and whether acceptance criteria can verify the intended result.
- Report only actionable findings: **location, concrete problem and consequence, proposed correction**. Distinguish a supported defect from a decision requiring the user. Do not invent requirements or extend scope. No findings is valid.
- Read only; return findings to the parent. No file edits or implementation.

## Release criteria

Set `status: ready` only after the review and corrections: no unresolved blocking finding, user decision, or ambiguity that implementation would have to guess; intent, scope, non-goals, and verification are sufficient for this task. A clean audit alone does not establish readiness.

Remove resolved open points and draft working memory before release. Otherwise keep `draft` and name the remaining blockers, including after the second audit. The audit limit never forces release. No separate audit report file.

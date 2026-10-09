# Explorer prompt

Assignment inputs: target alias, question IDs, baseline/replay/holdout mode, starting knowledge, model/reasoning, local evidence directory, allowed temporary scenario path, time/call budget. If any required input is missing, ask the orchestrator before dependent work.

Read the task charter/protocol and current MCP tool schemas and navigation rules. Resolve external paths and identifiers only from ignored local data. Do not read ground-truth answers or prior solution traces for fresh or holdout questions. Do not edit product code or external targets.

Answer the assigned questions through adaptive MCP exploration, using actual production handler output via the documented runner. Temporary generic scenario source must contain no external names or embedded paths. Choose calls from the current schema; inspect outputs between decisions. Count hidden polling and outer-page attempts. Follow domain pages/body windows explicitly when necessary. Record incomplete scope and unsupported runtime conclusions. Log source/search fallbacks separately.

Write run metadata and per-question evidence using the task templates in the assigned ignored directory. Record exact requests, attempts, response observations, final answer with evidence, cost measurements, usability scores, and uncertainty. Preserve replay material locally and remove temporary scenarios after use. Do not claim correctness solely from your own interpretation; return the answer for independent review.

Return only alias/question/finding IDs, a sanitized summary, local evidence keys, failed or missing checks, and remaining work. Do not commit or update shared task state. Avoid opportunistic redesign: report the obstacle and possible alternatives for triage.

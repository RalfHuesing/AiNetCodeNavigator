# Output format and token efficiency

## User question, 2026-10-03

The user asks whether the large JSON output is token-efficient and whether Markdown would be better. They accept some extra identifier tokens if it makes navigation reliable after edits; see [topic 01](01-symbol-identity-and-recovery.md).

## Verified current behavior

[NavigationToolSupport.Success](../../src/AiNetCodeNavigator/Mcp/Tools/NavigationToolSupport.cs) serializes payloads as indented JSON inside a text content block. Assembly routes use `SuccessCompact`, which is a more compact JSON text projection with deliberate newline boundaries. Bodies, skeletons and call graphs already use text/Markdown-style rendering in relevant routes. The server does not have one uniform all-JSON presentation.

MCP transport remains JSON-RPC regardless of the text payload's rendering. A Markdown body would change model-visible content, not eliminate the transport envelope. [McpToolResults](../../docs/mcp-tool-results.md) supports optional structured content; this does not prove that every route emits duplicate text and structured payloads or that every client counts both identically.

The inspected symbol-discovery sample repeats declaration information in `name`, `signature`, `docCommentId`, and locations, and includes both a DocCommentId and an `h:` handle. The dependency sample includes long internal type identity material on edges and full solution project references beside a targeted traversal. Payload selection and repeated identity representation deserve attention independently of punctuation.

## Local rendering experiment

Measured on 2026-10-03 using the existing local SharpToken assembly and `cl100k_base`, the same encoding used by the server. Input: [stored source find_symbol response](../../temp/mcp-test-360/raw_calls/01_source_local/03_find_symbol_response.txt). The experiment normalized CRLF to LF and removed trailing newlines, parsed the JSON payload, then compared the stored indented JSON, fully minified JSON and a recursive Markdown-list projection preserving all payload fields and values. All full-response variants used the same original status/analysis preamble.

| Rendering | Payload UTF-8 bytes | Payload tokens | Tokens with identical preamble |
| --- | ---: | ---: | ---: |
| Stored indented JSON | 1062 | 276 | 357 |
| Fully minified JSON | 692 | 181 | 262 |
| Markdown lists, all fields | 887 | 237 | 318 |

These are counts for one response, not an end-to-end benchmark, a production formatter result, or the audited Flash model's tokenizer. The recursive Markdown projection is not a designed table/grouped format. Fully minified JSON is also not a drop-in safe change: existing outer pagination preserves newline-delimited units, and a one-line payload can become an unfittable atomic unit. Counts do not prove better agent comprehension or navigation success.

In the same tokenizer, `h:fWSM` costs 4 tokens and `T:AiNetCodeNavigator.Core.CallTree.CallTreeBuilder` costs 12. The eight-token difference concerns this one declaration ID only; exact project ownership costs additional tokens and long generic member signatures can cost substantially more. Current find_symbol already outputs both of these values, so replacement cost cannot be calculated as adding the full DocCommentId length to every existing result. Recovery calls, rediscovery responses and repeated graph endpoints count toward total workflow cost.

## Current recommendation, not approved

Design small purpose-specific projections from typed internal results and measure before adopting a universal output syntax. Compare compact JSON with deliberate paging units, grouped Markdown/tables and a consistent compact text format on the same evidence and completeness.

- Keep owner, stable declaration reference, scope, analysis version, evidence kind, omissions and continuation/recovery information.
- Group shared target/project/path context once when unambiguous.
- Emit a declaration's reusable identity once per result and reference local row/node labels inside a graph where useful. Such local labels are not reusable navigation identifiers.
- Avoid repeated full internal type IDs on every edge when a symbol table can preserve exact identity.
- Select only relevant relationship levels and sections; avoid automatically emitting unrelated full-solution inventories.
- Render source code as source code without wrapping it in JSON string escaping.
- Determine how clients consume structured content before selecting any text/structured dual representation.
- Preserve atomic status/recovery controls and complete, safe outer/domain paging.

## Open decisions and proposed acceptance

Decide the default presentation, required fields, optional detail selection and machine-consumer requirements. Avoid adding many format switches without a demonstrated need. Measure full successful agent workflows and equivalent semantic scope, including all pages and errors; verify extraction of exact identifiers, ambiguous owners and evidence kinds. A Markdown conversion that omits fields cannot be credited as a pure format saving.

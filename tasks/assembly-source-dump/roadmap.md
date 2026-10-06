# Roadmap: assembly source dump

The [concept](Konzept.md) is the contract for these ordered implementation slices. Each completed slice includes its affected automated tests, current-state documentation, required repository verification, diff review and a conventional commit. Check a box only after its acceptance evidence exists.

- [x] **1. Establish the product and CLI boundary**
  - Add the separate executable project and minimum positional command contract to the solution. Update the product rule to permit this explicit offline exporter while keeping MCP navigation read-only and its stdout protocol-only.
  - Verify argument parsing and help/error behavior for zero, one and multiple input patterns; document the chosen glob limits and exit statuses.

- [x] **2. Resolve GAC references in Core**
  - Extend the existing metadata reference resolver with testable .NET Framework GAC candidate lookup and identity/architecture selection; feed proven paths into whole-project decompilation. Expose a cycle-safe reference closure that identifies any limit or ambiguity instead of silently omitting dependencies.
  - Verify local, runtime and GAC candidates; exact/mismatched versions and tokens; absent GAC; transitive references; traversal boundaries; and affected MCP assembly-navigation behavior using temporary fixtures.

- [ ] **3. Implement safe dump ownership and input planning**
  - Implement path/glob expansion, managed-DLL validation, automatic non-system dependency selection, path deduplication and output-name collision detection before creating or cleaning the dump. Apply the documented Microsoft/System filter, retaining excluded-edge reasons. Create or validate the exact root marker; restrict cleanup to selected direct children and reject reparse-point redirects.
  - Verify all marker, filter, closure, collision, containment, no-match, duplicate and untouched-child cases with filesystem tests.

- [ ] **4. Export whole projects per DLL**
  - Expose a narrow Core export facade around the existing decompiler and diagnostics. Delete each selected old child, stage and validate its generated files, create its relative-path solution entry point and per-DLL manifest, then publish its fresh child. Write `last-run.json` with completion/failure status; continue independent DLLs and return a nonzero status for failed ones.
  - Verify actual `.csproj`/`.cs`/`.sln` output for explicit and automatically selected GAC dependencies, dependency provenance, rerun replacement, failure leaving no stale selected child, output readability and unchanged input DLL bytes.

- [ ] **5. Integrate and audit the delivered tool**
  - Update README/current-state docs, local deployment, release packaging and agent usage guidance for the second executable. Run an end-to-end dump and rerun with representative generated DLLs; inspect the resulting file tree and console summary.
  - Run the official build, affected Fast and Integration tests and final routine solution gate; review the final diff against the concept, including cleanup safety and unchanged MCP protocol behavior.

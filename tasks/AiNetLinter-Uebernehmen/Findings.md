# Open Findings and Technical Debt

Point 2.3 remains open with its documented MSBuild structure boundary: declared wildcard imports and expressions unresolved during the loaded evaluation are not tracked as exact candidate paths. See [Cluster 2 integration review after fix round 1](Reviews/Cluster-02.md#integration-review-after-cluster-fix-round-1). Its three point audits are exhausted; the nested conditional import finding was fixed separately. Public tool composition and stdio acceptance remain later work.

The Cluster 3 point 3.3 Assembly session/consumer blocker was addressed by the Cluster 7.3 implementation; see [Cluster 7 review](Reviews/Cluster-07.md#point-73-assembly-handoffs-and-session-lifecycle). Its three point audits are already exhausted, so this implementation does not request another point 3.3 audit. MCP stdio dispatch remains part of the later host/tool-registration work.

Resolved integration findings and closure are recorded in [Cluster 6 integration review 3](Reviews/Cluster-06.md#cluster-6-integration-review-3-after-fix-round-2).

Point 7.3's three audits are complete with no open point findings. The three audit-1 lifecycle issues and audit-2 recovery issue were fixed and accepted; evidence is recorded in the [Cluster 7 point 7.3 review](Reviews/Cluster-07.md#point-73-assembly-handoffs-and-session-lifecycle). MCP stdio dispatch remains a later host and end-to-end acceptance gate.

Cluster 7 integration review 1 found a P2 cache/session mismatch: a same-identity replacement of a resolved reference could create a new resident generation while reusing decompiled source produced against the previous reference bytes. Cache manifests now bind both reads and publishes to the content fingerprint captured for the reference snapshot; the fix was accepted in [Cluster 7 integration review 2](Reviews/Cluster-07.md#cluster-7-integration-review-2-after-fix-round-1). No Cluster 7 integration finding remains open.

Cluster 8.1 implementation evidence and the independent point audit are tracked in [Cluster 8 review](Reviews/Cluster-08.md#point-81-budgeting-and-truncation). No public MCP invocation is claimed before the Cluster 9/11 host acceptance work.

Cluster 8.1 independent point audit 1/3 found one P1 and two P2 contract gaps: an unretryable `minimumResponseBytes` above the public maximum, accepted mid-line continuation offsets, and error responses exceeding a requested token cap. The fixes were accepted with no new point finding in [independent audit 2/3](Reviews/Cluster-08.md#point-81-independent-audit-23). Point 8.1 is closed; public stdio acceptance remains later Cluster 9/11 work.

Point 8.2 is closed after [final independent audit 3/3](Reviews/Cluster-08.md#point-82-independent-audit-33--accepted) accepted remediation `b7895eef60e834443f0e094989a8f7c7924e09a9`. No point finding remains open; the three-audit limit is exhausted. Public registration and stdio acceptance remain later Cluster 9/11 work.

Point 8.3 is closed after [final independent audit 3/3](Reviews/Cluster-08.md#point-83-independent-audit-33--accepted) accepted remediation `7db5c08a0e408df9fca2a921ea1191af86f8ba3f`. No point finding remains open; the three-audit limit is exhausted. Cluster 8 integration and public registration/stdio acceptance remain separate work.

Point 8.4 is closed after [final independent audit 3/3](Reviews/Cluster-08.md#point-84-independent-audit-33--accepted) accepted remediation `2da79dfaaefddd3002f32dbe449707ad3f3fc24c`. No point finding remains open; the three-audit limit is exhausted. Point 8.5, Cluster 8 integration, and public host/tool/stdio acceptance remain separate work.

Point 8.5's public per-tool acceptance remains pending Cluster 9.2's 20 navigation registrations. Cluster 9.1 now runs a real stdio host and verifies the two maintenance tools, but this does not establish navigation-tool success, truncation, continuation, or input contracts. Keep 8.5 and its audit checkbox open at 0/3 until those registrations exist. See the [8.5 dependency assessment](Reviews/Cluster-08.md#point-85-dependency-assessment--point-audits-not-started) for the acceptance matrix.

[Cluster 8 integration review 1](Reviews/Cluster-08.md#cluster-8-integration-review-1--partial-scope-public-contracts-pending) found no new interface defect in the currently connected internal scope. Review count 1, fix rounds 0. Cluster 8 remains open for the documented 8.5/9.2 public-tool dependency; this partial review consumes no 8.5 point audit. Point 8.1 is closed after 2/3 audits, while 8.2-8.4 are closed after 3/3.

Point 9.1 is closed after [independent audit 2/3](Reviews/Cluster-09.md#point-91-independent-audit-23--accepted) accepted remediation `3cd6919e9c2c8f2c543082cd93fb662f2e406ae5` for all three audit-1 P2 maintenance-contract findings. Exact conservative health recovery pairs return full snapshots; budget-rejected reloads preserve settings; unchanged reloads retain their version. Own focused gates passed 10 fast and 4 real-host integration cases, with separate stdio/direct reproduction evidence. No point finding remains open; no third audit is requested without a new concrete finding. Navigation registration, 8.5 (0/3), broader lifecycle acceptance, and complete product acceptance remain later work.

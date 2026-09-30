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

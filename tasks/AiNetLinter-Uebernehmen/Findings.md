# Open Findings and Technical Debt

The Cluster 3 point 3.3 Assembly session/consumer blocker was addressed by the Cluster 7.3 implementation; see [Cluster 7 review](Reviews/Cluster-07.md#point-73-assembly-handoffs-and-session-lifecycle). Its three point audits are already exhausted, so this implementation does not request another point 3.3 audit. MCP stdio dispatch remains part of the later host/tool-registration work.

Resolved integration findings and closure are recorded in [Cluster 6 integration review 3](Reviews/Cluster-06.md#cluster-6-integration-review-3-after-fix-round-2).

Point 7.3's three audits are complete with no open point findings. The three audit-1 lifecycle issues and audit-2 recovery issue were fixed and accepted; evidence is recorded in the [Cluster 7 point 7.3 review](Reviews/Cluster-07.md#point-73-assembly-handoffs-and-session-lifecycle). MCP stdio dispatch remains a later host and end-to-end acceptance gate.

Cluster 7 integration review 1 found a P2 cache/session mismatch: a same-identity replacement of a resolved reference could create a new resident generation while reusing decompiled source produced against the previous reference bytes. Cache manifests now bind both reads and publishes to the content fingerprint captured for the reference snapshot; the fix was accepted in [Cluster 7 integration review 2](Reviews/Cluster-07.md#cluster-7-integration-review-2-after-fix-round-1). No Cluster 7 integration finding remains open.

Cluster 8.1 implementation evidence and the independent point audit are tracked in [Cluster 8 review](Reviews/Cluster-08.md#point-81-budgeting-and-truncation). No public MCP invocation is claimed before the Cluster 9/11 host acceptance work.

Cluster 8.1 independent point audit 1/3 found one P1 and two P2 contract gaps: an unretryable `minimumResponseBytes` above the public maximum, accepted mid-line continuation offsets, and error responses exceeding a requested token cap. The fixes were accepted with no new point finding in [independent audit 2/3](Reviews/Cluster-08.md#point-81-independent-audit-23). Point 8.1 is closed; public stdio acceptance remains later Cluster 9/11 work.

Point 8.2's audit-2 P2 findings are remediated in the current implementation: retry budgets now include the status prefix of the selected complete or truncated success projection, and optional context cannot remove any part of the exact required error text. The regression tests were red against the prior implementation and pass after the fix. Final independent audit 3/3 is pending; public registration and stdio acceptance belong to Clusters 9/11.

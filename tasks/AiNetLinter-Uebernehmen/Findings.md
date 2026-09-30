# Open Findings and Technical Debt

The Cluster 3 point 3.3 Assembly session/consumer blocker was addressed by the Cluster 7.3 implementation; see [Cluster 7 review](Reviews/Cluster-07.md#point-73-assembly-handoffs-and-session-lifecycle). Its three point audits are already exhausted, so this implementation does not request another point 3.3 audit. MCP stdio dispatch remains part of the later host/tool-registration work.

Resolved integration findings and closure are recorded in [Cluster 6 integration review 3](Reviews/Cluster-06.md#cluster-6-integration-review-3-after-fix-round-2).

Point 7.3 audit 1/3 found three Assembly session lifecycle issues (one P1, two P2): stale API after a failed refresh, reference changes ignored by resident reuse, and a soft 32-target bound when every entry is active. All three fixes and red/green regression evidence are recorded in [Cluster 7 point 7.3 audit 1 remediation](Reviews/Cluster-07.md#point-73-audit-13-remediation). The independent point audit remains open for follow-up; this implementation does not perform or close an audit.

Point 7.3 audit 2/3 accepted those three fixes but found a P1 resident-session recovery failure when the original valid DLL returns after a failed refresh. See [Cluster 7 point 7.3 audit 2](Reviews/Cluster-07.md#point-73-independent-audit-23) for the reproduction and acceptance condition. The point audit remains open.

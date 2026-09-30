# Open Findings and Technical Debt

- **Cluster 3, point 3.3 (audit 3/3; local blocker):** Assembly `inspect_assembly` handoffs still lack a resident Assembly consumer/session and follow-up roundtrips. This depends on Cluster 7; point 3.3 stays open. Source identity and roundtrip findings are closed. Evidence and acceptance criteria: [Cluster-03 review](Reviews/Cluster-03.md#independent-audit-33-of-point-33).
- **Cluster 4, point 4.3 (audit 1/3):** Field/event initializer bodies leak into skeleton signatures, and multi-variable declarations expose only the first symbol's handoff. Evidence and acceptance criteria: [Cluster-04 review](Reviews/Cluster-04.md#independent-audit-13-of-point-43).

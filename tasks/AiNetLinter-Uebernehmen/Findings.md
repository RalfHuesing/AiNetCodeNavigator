# Open Findings and Technical Debt

- **Cluster 3, point 3.3 (audit 3/3; local blocker):** Assembly `inspect_assembly` handoffs still lack a resident Assembly consumer/session and follow-up roundtrips. This depends on Cluster 7; point 3.3 stays open. Source identity and roundtrip findings are closed. Evidence and acceptance criteria: [Cluster-03 review](Reviews/Cluster-03.md#independent-audit-33-of-point-33).
- **Cluster 5, point 5.5 (audit 2/3):** Target traversal remains batch-local and unbounded by visited nodes; document continuation lacks regression proof for later dependencies and documents beyond 1000. Generic/qualified type edges are fixed. Evidence and acceptance criteria: [Cluster-05 review](Reviews/Cluster-05.md#independent-audit-23-of-point-55).

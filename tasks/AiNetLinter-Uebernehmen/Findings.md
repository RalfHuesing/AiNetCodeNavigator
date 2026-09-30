# Open Findings and Technical Debt

- **Cluster 3, point 3.3 (audit 3/3; local blocker):** Assembly `inspect_assembly` handoffs still lack a resident Assembly consumer/session and follow-up roundtrips. This depends on Cluster 7; point 3.3 stays open. Source identity and roundtrip findings are closed. Evidence and acceptance criteria: [Cluster-03 review](Reviews/Cluster-03.md#independent-audit-33-of-point-33).
- **Cluster 5, point 5.5 (audit 1/3):** Target-centered file/type traversal is absent, document caps cannot continue, and generic source type edges are missed. Evidence and acceptance criteria: [Cluster-05 review](Reviews/Cluster-05.md#independent-audit-13-of-point-55).

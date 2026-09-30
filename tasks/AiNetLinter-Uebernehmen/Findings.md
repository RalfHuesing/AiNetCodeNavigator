# Open Findings and Technical Debt

- **Cluster 3, point 3.3 (audit 3/3; local blocker):** Assembly `inspect_assembly` handoffs still lack a resident Assembly consumer/session and follow-up roundtrips. This depends on Cluster 7; point 3.3 stays open. Source identity and roundtrip findings are closed. Evidence and acceptance criteria: [Cluster-03 review](Reviews/Cluster-03.md#independent-audit-33-of-point-33).
- **Cluster 5, point 5.5 (audit 3/3; technical debt):** The merger can falsely mark complete multi-page relationship input incomplete when collections have different lengths under one shared offset. Cross-window traversal and the 1002-document continuation are fixed. Evidence and acceptance criteria: [Cluster-05 review](Reviews/Cluster-05.md#independent-audit-33-of-point-55).

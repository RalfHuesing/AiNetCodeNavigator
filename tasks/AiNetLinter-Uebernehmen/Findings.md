# Open Findings and Technical Debt

- **Cluster 3, point 3.3 (audit 3/3; local blocker):** Assembly `inspect_assembly` handoffs still lack a resident Assembly consumer/session and follow-up roundtrips. This depends on Cluster 7; point 3.3 stays open. Source identity and roundtrip findings are closed. Evidence and acceptance criteria: [Cluster-03 review](Reviews/Cluster-03.md#independent-audit-33-of-point-33).
- **Cluster 7, point 7.1 (audit 1/3; P2):** Modified cached decompiled source is accepted without a content digest, and timeout recovery scans partial output without the deadline token. Evidence and acceptance criteria: [Cluster-07 review](Reviews/Cluster-07.md#independent-audit-13-of-point-71).

Resolved integration findings and closure are recorded in [Cluster 6 integration review 3](Reviews/Cluster-06.md#cluster-6-integration-review-3-after-fix-round-2).

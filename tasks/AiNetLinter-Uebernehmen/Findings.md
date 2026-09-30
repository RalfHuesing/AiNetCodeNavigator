# Open Findings and Technical Debt

- **Cluster 6 integration (first review, fix round 0):** Namespace discovery can disagree with index scope's C# coverage, generated-source visibility differs from default symbol navigation, and project-name whitespace is handled inconsistently. Evidence and acceptance criteria: [Cluster-06 review](Reviews/Cluster-06.md#cluster-6-integration-review-1-fix-round-0).
- **Cluster 3, point 3.3 (audit 3/3; local blocker):** Assembly `inspect_assembly` handoffs still lack a resident Assembly consumer/session and follow-up roundtrips. This depends on Cluster 7; point 3.3 stays open. Source identity and roundtrip findings are closed. Evidence and acceptance criteria: [Cluster-03 review](Reviews/Cluster-03.md#independent-audit-33-of-point-33).

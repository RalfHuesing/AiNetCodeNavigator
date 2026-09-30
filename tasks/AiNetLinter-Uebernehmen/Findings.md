# Open Findings and Technical Debt

- **Cluster 3, point 3.3 (audit 3/3; local blocker):** Assembly `inspect_assembly` handoffs still lack a resident Assembly consumer/session and follow-up roundtrips. This depends on Cluster 7; point 3.3 stays open. Source identity and roundtrip findings are closed. Evidence and acceptance criteria: [Cluster-03 review](Reviews/Cluster-03.md#independent-audit-33-of-point-33).
- **Cluster 4, point 4.8 (audit 2/3):** `TestContextPayload` and Feature Context Markdown now disclose `static-test-candidates-only`, but the structured `FeatureContextPayload.Tests` result still has no evidence-mode marker. This remaining P2 keeps the audit open. Evidence and acceptance criteria: [Cluster-04 review](Reviews/Cluster-04.md#independent-audit-23-of-point-48).

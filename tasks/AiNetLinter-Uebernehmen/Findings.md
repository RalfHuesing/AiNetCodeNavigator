# Open Findings and Technical Debt

- **Cluster 6 integration (review 2, after fix round 1):** `find_symbol` still searches the entire Roslyn solution without an explicit C# project-language boundary, while namespace tree now excludes non-C# projects and index scope marks their documents as uncovered. Evidence and acceptance criteria: [Cluster-06 review](Reviews/Cluster-06.md#cluster-6-integration-review-2-after-fix-round-1).
- **Cluster 3, point 3.3 (audit 3/3; local blocker):** Assembly `inspect_assembly` handoffs still lack a resident Assembly consumer/session and follow-up roundtrips. This depends on Cluster 7; point 3.3 stays open. Source identity and roundtrip findings are closed. Evidence and acceptance criteria: [Cluster-03 review](Reviews/Cluster-03.md#independent-audit-33-of-point-33).

Resolved integration findings are recorded in [Cluster 6 fix round 1](Reviews/Cluster-06.md#cluster-6-integration-fix-round-1).

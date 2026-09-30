# Get Feature Context

`FeatureContextScanner.ScanAsync` resolves one source symbol and combines its declaration/signature, incoming callers, and statically matched test recommendations. The declaration and source callers carry opaque `h:` handoffs when a stable source identity is available; test-method handoffs point to the attributed method. These IDs can be passed back to the scanner to resolve those symbols.

Caller scope is applied before totals and limits are calculated. `all` includes callers from every source file, `production` excludes files classified as tests, and `tests` keeps those files. The request defaults to 20 callers and 20 test candidates. Each requested maximum is clamped to 1 through 50, results are sorted deterministically before truncation, and the payload reports scoped totals plus truncation flags.

An unresolved ordinary name returns `null`. Invalid or unresolvable handoffs return a payload with a structured navigation error. Null requests/solutions and blank identifiers throw argument exceptions. Markdown rendering rejects a null payload with `ArgumentNullException`.

Test entries are heuristic candidates produced by `TestRecommendationBuilder`; they do not establish execution, coverage, or a semantic relationship. The feature-context result contains navigation data only and does not include Linter violations or quality metrics.

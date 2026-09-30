# Assembly Decompilation Core

`AssemblyFingerprintCalculator` canonicalizes an assembly path and fingerprints its length, UTC modification time, and SHA-256 content. The decompilation cache key also includes the decompiler version, effective options, and cache schema version.

`AssemblyDecompilationAdapter` uses ICSharpCode.Decompiler to materialize a managed assembly into a temporary staging directory. It parses generated C# documents for syntax errors and reports incomplete output with diagnostics. It does not write to the analyzed assembly; the FastTests compare the input bytes and timestamp before and after decompilation. Native PE files and invalid managed images return diagnostics instead of escaping as exceptions.

`AssemblyDecompilationCache` publishes complete or partial, analysable generations behind a current-generation pointer. It validates the manifest against the requested content fingerprint, cache key, and resolved references before returning a generation. Concurrent identical publications resolve to an existing published generation. Incomplete decompilations are not eligible for publication.

`AssemblyRoslynWorkspaceFactory` creates an in-memory `AdhocWorkspace` project from the decompiled documents. It excludes the analyzed assembly from metadata references, adds the core library when needed, and maps each generated document to its assembly origin. Empty document sets and canceled requests fail before a snapshot is returned. The snapshot owns the workspace lifetime and disposes it when the snapshot is disposed.

The verified component contracts are covered by [assembly decompilation FastTests](../../tests/AiNetCodeNavigator.FastTests/Assemblies/AssemblyDecompilationBoundaryTests.cs) and [cache roundtrip FastTests](../../tests/AiNetCodeNavigator.FastTests/Assemblies/AssemblyDecompilationCacheTests.cs).

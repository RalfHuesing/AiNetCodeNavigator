#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Definierte Fehlercodes für maschinenlesbares Error-Reporting in AiNetCodeNavigator.
/// </summary>
public static class NavigationErrorCodes
{
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string SolutionNotFound = "SOLUTION_NOT_FOUND";
    public const string SolutionNotLoaded = "SOLUTION_NOT_LOADED";
    public const string SymbolNotFound = "SYMBOL_NOT_FOUND";
    public const string AmbiguousSymbol = "AMBIGUOUS_SYMBOL";
    public const string AssemblyTargetUnsupported = "ASSEMBLY_TARGET_UNSUPPORTED";
    public const string AssemblySessionLimit = "ASSEMBLY_SESSION_LIMIT";
    public const string ProjectTargetUnsupported = "PROJECT_TARGET_UNSUPPORTED";
    public const string InvalidAssembly = "INVALID_ASSEMBLY";
    public const string TargetUnreadable = "TARGET_UNREADABLE";
    public const string InvalidHandoff = "INVALID_HANDOFF";
    public const string HandoffUnknown = "HANDOFF_UNKNOWN";
    public const string HandoffCounterUnavailable = "HANDOFF_COUNTER_UNAVAILABLE";
    public const string TargetMismatch = "TARGET_MISMATCH";
    public const string StaleSnapshot = "STALE_SNAPSHOT";
    public const string UnsupportedIdentifier = "UNSUPPORTED_IDENTIFIER";
    public const string ResponseBudgetTooSmall = "RESPONSE_BUDGET_TOO_SMALL";
    public const string WorkspaceDiagnostic = "WORKSPACE_DIAGNOSTIC";
}

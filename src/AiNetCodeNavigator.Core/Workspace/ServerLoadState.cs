#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Lifecycle state of solution loading for resident sessions.
/// </summary>
public enum ServerLoadState
{
    Loading,
    Loaded,
    LoadFailed,
}

#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Lebenszyklus-Zustand des Solution-Ladens für residente Sessions.
/// </summary>
public enum ServerLoadState
{
    Loading,
    Loaded,
    LoadFailed,
}

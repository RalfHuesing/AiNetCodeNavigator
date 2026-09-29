#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Unveränderlicher Werttyp einer geladenen Projektdefinition.
/// Der Pfad ist absolut und existenzgeprüft.
/// </summary>
public sealed record ProjectDefinition(string SolutionPath);

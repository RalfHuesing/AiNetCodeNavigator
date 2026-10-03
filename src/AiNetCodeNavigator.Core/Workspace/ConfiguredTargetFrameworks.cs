#nullable enable

using System.Collections.Generic;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>Configured target framework values captured with the loaded solution structure.</summary>
public sealed record ConfiguredTargetFrameworks(bool IsKnown, IReadOnlyList<string> Values);

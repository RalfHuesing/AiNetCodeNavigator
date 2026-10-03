using System.Reflection;
using ModelContextProtocol.Protocol;

namespace AiNetCodeNavigator.Mcp;

internal static class ServerBuildIdentity
{
    public const string Name = "AiNetCodeNavigator";

    public static Implementation Create() => new()
    {
        Name = Name,
        Version = GetVersion(typeof(ServerBuildIdentity).Assembly),
    };

    internal static string GetVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}

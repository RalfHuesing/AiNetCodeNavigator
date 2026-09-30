namespace AiNetCodeNavigator.Mcp.Formatting;

internal static class McpResponseBudgetLimits
{
    internal const int DefaultBytes = 16 * 1024;
    internal const int MinimumBytes = 512;
    internal const int MaximumBytes = 65_536;

    internal static bool IsPublicBudget(int value) =>
        value >= MinimumBytes && value <= MaximumBytes;
}

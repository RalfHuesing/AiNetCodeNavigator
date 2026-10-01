#nullable enable

using System.Text;

namespace AiNetCodeNavigator.Core.Workspace;

/// <summary>
/// Creates structured error messages for machine-readable parsing by LLM agents.
/// Format: [ERROR]: {code}: {message}[\n  context:   {context}][\n  hint:      {hint}][\n  fieldPath: {fieldPath}]
/// </summary>
public static class NavigatorErrorFormatter
{
    public static string Format(
        string code,
        string message,
        string? context = null,
        string? hint = null,
        string? fieldPath = null)
    {
        var sb = new StringBuilder();
        sb.Append($"[ERROR]: {code}: {message}");
        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.Append($"\n  context:   {context}");
        }
        if (!string.IsNullOrWhiteSpace(hint))
        {
            sb.Append($"\n  hint:      {hint}");
        }
        if (!string.IsNullOrWhiteSpace(fieldPath))
        {
            sb.Append($"\n  fieldPath: {fieldPath}");
        }

        return sb.ToString();
    }
}

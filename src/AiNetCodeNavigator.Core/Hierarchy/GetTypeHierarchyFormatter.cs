#nullable enable

using System;
using System.Text;

namespace AiNetCodeNavigator.Core.Hierarchy;

/// <summary>
/// Renders the type hierarchy as formatted Markdown/text.
/// </summary>
public static class GetTypeHierarchyFormatter
{
    public static string FormatText(TypeHierarchyPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.IsSuccess) return payload.ErrorMessage!;

        var sb = new StringBuilder();
        sb.AppendLine($"# Type hierarchy for {payload.TypeName}");
        sb.AppendLine();

        sb.AppendLine("## Base classes:");
        if (payload.BaseTypes.Count == 0)
        {
            sb.AppendLine("No base class (System.Object).");
        }
        else
        {
            var indent = "";
            foreach (var b in payload.BaseTypes)
            {
                var loc = !string.IsNullOrEmpty(b.FilePath) ? $" ({b.FilePath}:{b.Line})" : "";
                var handoff = b.HandoffId != null ? $" [handoff: `{b.HandoffId}`]" : "";
                sb.AppendLine($"{indent}- {b.Name}{loc}{handoff}");
                indent += "  ";
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Implemented interfaces:");
        if (payload.Interfaces.Count == 0)
        {
            sb.AppendLine("No interfaces.");
        }
        else
        {
            foreach (var i in payload.Interfaces)
            {
                var loc = !string.IsNullOrEmpty(i.FilePath) ? $" ({i.FilePath}:{i.Line})" : "";
                var handoff = i.HandoffId != null ? $" [handoff: `{i.HandoffId}`]" : "";
                sb.AppendLine($"- {i.Name}{loc}{handoff}");
            }
        }
        sb.AppendLine();

        sb.AppendLine($"## {payload.SubtypesHeading}");
        if (payload.Subtypes.Count == 0)
        {
            sb.AppendLine("No matches.");
        }
        else
        {
            foreach (var s in payload.Subtypes)
            {
                var loc = !string.IsNullOrEmpty(s.FilePath) ? $" ({s.FilePath}:{s.Line})" : "";
                var handoff = s.HandoffId != null ? $" [handoff: `{s.HandoffId}`]" : "";
                sb.AppendLine($"- {s.Name} [{s.Kind}]{loc}{handoff}");
            }

            if (payload.IsTruncated)
            {
                sb.AppendLine($"... ({payload.TotalSubtypes - payload.Subtypes.Count} more truncated)");
            }
        }

        return sb.ToString().TrimEnd();
    }
}

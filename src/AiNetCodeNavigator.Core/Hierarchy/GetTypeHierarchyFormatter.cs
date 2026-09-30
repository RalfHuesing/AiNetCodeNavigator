#nullable enable

using System;
using System.Text;

namespace AiNetCodeNavigator.Core.Hierarchy;

/// <summary>
/// Rendert die Typ-Hierarchie als formatierte Markdown-/Textstruktur.
/// </summary>
public static class GetTypeHierarchyFormatter
{
    public static string FormatText(TypeHierarchyPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.IsSuccess) return payload.ErrorMessage!;

        var sb = new StringBuilder();
        sb.AppendLine($"# Typ-Hierarchie für {payload.TypeName}");
        sb.AppendLine();

        sb.AppendLine("## Basisklassen:");
        if (payload.BaseTypes.Count == 0)
        {
            sb.AppendLine("Keine Basisklasse (System.Object).");
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

        sb.AppendLine("## Implementierte Interfaces:");
        if (payload.Interfaces.Count == 0)
        {
            sb.AppendLine("Keine Interfaces.");
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
            sb.AppendLine("Keine Treffer.");
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
                sb.AppendLine($"... ({payload.TotalSubtypes - payload.Subtypes.Count} weitere abgeschnitten)");
            }
        }

        return sb.ToString().TrimEnd();
    }
}

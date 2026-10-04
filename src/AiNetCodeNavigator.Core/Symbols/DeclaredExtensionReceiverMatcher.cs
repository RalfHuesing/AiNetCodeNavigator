namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Matches declared receiver names; does not test expression applicability.</summary>
public static class DeclaredExtensionReceiverMatcher
{
    public static bool Matches(string receiver, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var normalizedReceiver = NormalizeType(receiver);
        var normalizedFilter = NormalizeType(filter.Trim());
        return string.Equals(normalizedReceiver, normalizedFilter, StringComparison.OrdinalIgnoreCase)
            || normalizedReceiver.EndsWith("." + normalizedFilter, StringComparison.OrdinalIgnoreCase)
            || normalizedFilter.EndsWith("." + normalizedReceiver, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeType(string value) => value switch
    {
        "string" => "System.String",
        "object" => "System.Object",
        "bool" => "System.Boolean",
        "byte" => "System.Byte",
        "char" => "System.Char",
        "decimal" => "System.Decimal",
        "double" => "System.Double",
        "float" => "System.Single",
        "int" => "System.Int32",
        "long" => "System.Int64",
        "short" => "System.Int16",
        _ => value.Replace("global::", string.Empty, StringComparison.Ordinal),
    };
}

#nullable enable

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>A declaration reference whose owner is supplied by the selected target.</summary>
public abstract record StableSymbolReference(string DeclarationId)
{
    public sealed record Source(string ProjectPath, string Id) : StableSymbolReference(Id);

    public sealed record Assembly(string SimpleName, string Id) : StableSymbolReference(Id);
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AiNetCodeNavigator.Core.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetCodeNavigator.Core.Skeletons;

/// <summary>
/// Extrahiert Typ-Skelette (Signaturen + Metadaten ohne Methodenrümpfe) aus einem C#-Syntaxbaum via SemanticModel.
/// </summary>
public sealed class SkeletonSyntaxWalker : CSharpSyntaxWalker
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private readonly SemanticModel _semanticModel;
    private readonly string _relativePath;
    private readonly Func<string?, string?>? _formatSymbolId;
    private readonly List<SkeletonTypeInfo> _types = [];
    private string _currentNamespace = "";

    public IReadOnlyList<SkeletonTypeInfo> Types => _types;

    public SkeletonSyntaxWalker(
        SemanticModel semanticModel,
        string relativePath,
        Func<string?, string?>? formatSymbolId = null)
        : base(SyntaxWalkerDepth.Node)
    {
        _semanticModel = semanticModel;
        _relativePath = relativePath;
        _formatSymbolId = formatSymbolId;
    }

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        var previous = _currentNamespace;
        _currentNamespace = node.Name.ToString();
        base.VisitNamespaceDeclaration(node);
        _currentNamespace = previous;
    }

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        _currentNamespace = node.Name.ToString();
        base.VisitFileScopedNamespaceDeclaration(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        if (IsNestedType(node)) return;
        _types.Add(BuildTypeInfo("class", node));
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        if (IsNestedType(node)) return;
        var kind = node.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record";
        _types.Add(BuildTypeInfo(kind, node));
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        if (IsNestedType(node)) return;
        _types.Add(BuildTypeInfo("interface", node));
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        if (IsNestedType(node)) return;
        _types.Add(BuildTypeInfo("struct", node));
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        if (IsNestedType(node)) return;
        var typeSymbol = _semanticModel.GetDeclaredSymbol(node);
        var typeId = FormatSymbolId(typeSymbol?.GetDocumentationCommentId());
        var members = node.Members
            .Select(m => new SkeletonMemberInfo(
                SkeletonMemberKind.Field,
                m.Identifier.Text,
                null,
                FormatSymbolId(TryCreateEnumFieldId(typeSymbol, m.Identifier.Text))))
            .ToList();

        _types.Add(new SkeletonTypeInfo(
            _currentNamespace,
            "enum",
            BuildModifiers(node.Modifiers),
            node.Identifier.Text,
            null,
            _relativePath,
            members,
            typeId));
    }

    private static bool IsNestedType(SyntaxNode node) =>
        node.Parent is TypeDeclarationSyntax;

    private SkeletonTypeInfo BuildTypeInfo(string typeKind, TypeDeclarationSyntax node)
    {
        var fullName = node.Identifier.Text + (node.TypeParameterList?.ToString() ?? "");
        var typeSymbol = _semanticModel.GetDeclaredSymbol(node);
        var baseTypes = BuildBaseTypesDisplay(node, typeSymbol);
        var typeId = FormatSymbolId(typeSymbol?.GetDocumentationCommentId());
        var memberInfos = ExtractMembers(node.Members);

        if (node is RecordDeclarationSyntax recordDecl && recordDecl.ParameterList != null && typeSymbol is INamedTypeSymbol recordSymbol)
        {
            foreach (var param in recordDecl.ParameterList.Parameters)
            {
                var propType = param.Type?.ToString() ?? "object";
                var propName = param.Identifier.Text;
                var accessor = typeKind == "record struct" ? "{ get; set; }" : "{ get; init; }";
                var sig = $"public {propType} {propName} {accessor}";
                memberInfos.Add(new SkeletonMemberInfo(
                    SkeletonMemberKind.Property,
                    NormalizeWhitespace(sig),
                    null,
                    FormatSymbolId(TryCreateRecordParameterId(recordSymbol, propName))));
            }
        }

        return new SkeletonTypeInfo(
            _currentNamespace,
            typeKind,
            BuildModifiers(node.Modifiers),
            fullName,
            baseTypes,
            _relativePath,
            memberInfos,
            typeId);
    }

    private static string? BuildBaseTypesDisplay(TypeDeclarationSyntax node, ISymbol? typeSymbol)
    {
        if (node.BaseList != null)
        {
            return ": " + node.BaseList.Types.ToString();
        }

        if (typeSymbol is INamedTypeSymbol { BaseType.SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType) } named)
        {
            return $": {named.BaseType!.ToDisplayString()}";
        }

        return null;
    }

    private List<SkeletonMemberInfo> ExtractMembers(SyntaxList<MemberDeclarationSyntax> members)
    {
        var result = new List<SkeletonMemberInfo>();

        foreach (var member in members)
        {
            var info = member switch
            {
                FieldDeclarationSyntax f => BuildFieldInfo(f),
                ConstructorDeclarationSyntax c => BuildConstructorInfo(c),
                PropertyDeclarationSyntax p => BuildPropertyInfo(p),
                MethodDeclarationSyntax m => BuildMethodInfo(m),
                EventFieldDeclarationSyntax e => BuildEventInfo(e),
                _ => null,
            };

            if (info != null) result.Add(info);
        }

        return result;
    }

    private SkeletonMemberInfo BuildFieldInfo(FieldDeclarationSyntax node)
    {
        var sig = NormalizeWhitespace(node.ToString().Trim().TrimEnd(';') + ";");
        var firstVar = node.Declaration.Variables.FirstOrDefault();
        var symbol = firstVar is null ? null : _semanticModel.GetDeclaredSymbol(firstVar);
        return new SkeletonMemberInfo(SkeletonMemberKind.Field, sig, null, FormatSymbolId(symbol?.GetDocumentationCommentId()));
    }

    private SkeletonMemberInfo BuildPropertyInfo(PropertyDeclarationSyntax node)
    {
        var accessors = node.AccessorList != null
            ? "{ " + string.Join(" ", node.AccessorList.Accessors.Select(a => a.Keyword.Text + ";")) + " }"
            : "=> /* computed */";
        var sig = $"{BuildModifiers(node.Modifiers, node.Parent)} {node.Type} {node.Identifier.Text} {accessors}";
        var symbol = _semanticModel.GetDeclaredSymbol(node) as IPropertySymbol;
        return new SkeletonMemberInfo(SkeletonMemberKind.Property, NormalizeWhitespace(sig), null, FormatSymbolId(symbol?.GetDocumentationCommentId()));
    }

    private SkeletonMemberInfo BuildConstructorInfo(ConstructorDeclarationSyntax node)
    {
        var paramList = FormatParameters(node.ParameterList);
        var sig = $"{BuildModifiers(node.Modifiers, node.Parent)} {node.Identifier.Text}({paramList})";
        var symbol = _semanticModel.GetDeclaredSymbol(node) as IMethodSymbol;
        return new SkeletonMemberInfo(SkeletonMemberKind.Constructor, NormalizeWhitespace(sig), null, FormatSymbolId(symbol?.GetDocumentationCommentId()));
    }

    private SkeletonMemberInfo BuildMethodInfo(MethodDeclarationSyntax node)
    {
        var returnType = node.ReturnType.ToString();
        var typeParams = node.TypeParameterList?.ToString() ?? "";
        var paramList = FormatParameters(node.ParameterList);
        var sig = $"{BuildModifiers(node.Modifiers, node.Parent)} {returnType} {node.Identifier.Text}{typeParams}({paramList})";
        var symbol = _semanticModel.GetDeclaredSymbol(node) as IMethodSymbol;
        var kind = ClassifyMethodKind(node.Modifiers, node.Parent);
        return new SkeletonMemberInfo(kind, NormalizeWhitespace(sig), null, FormatSymbolId(symbol?.GetDocumentationCommentId()));
    }

    private SkeletonMemberInfo BuildEventInfo(EventFieldDeclarationSyntax node)
    {
        var sig = NormalizeWhitespace(node.ToString().Trim().TrimEnd(';') + ";");
        var firstVar = node.Declaration.Variables.FirstOrDefault();
        var symbol = firstVar is null ? null : _semanticModel.GetDeclaredSymbol(firstVar);
        return new SkeletonMemberInfo(SkeletonMemberKind.Event, sig, null, FormatSymbolId(symbol?.GetDocumentationCommentId()));
    }

    private static SkeletonMemberKind ClassifyMethodKind(SyntaxTokenList modifiers, SyntaxNode? parent)
    {
        if (parent is InterfaceDeclarationSyntax) return SkeletonMemberKind.PublicMethod;
        if (modifiers.Any(SyntaxKind.PublicKeyword)) return SkeletonMemberKind.PublicMethod;
        if (modifiers.Any(SyntaxKind.InternalKeyword)) return SkeletonMemberKind.InternalMethod;
        return SkeletonMemberKind.PrivateMethod;
    }

    private static string FormatParameters(ParameterListSyntax? paramList)
    {
        if (paramList is null) return "";
        return string.Join(", ", paramList.Parameters.Select(p =>
        {
            var mods = p.Modifiers.ToString();
            var modPrefix = string.IsNullOrEmpty(mods) ? "" : mods + " ";
            var typePart = p.Type?.ToString() ?? "var";
            var defaultPart = p.Default != null ? $" = {p.Default.Value}" : "";
            return $"{modPrefix}{typePart} {p.Identifier.Text}{defaultPart}".Trim();
        }));
    }

    private static string BuildModifiers(SyntaxTokenList modifiers, SyntaxNode? parent = null)
    {
        if (parent is InterfaceDeclarationSyntax) return "public";
        var list = modifiers.Select(m => m.Text).ToList();
        if (list.Count == 0) return "private";
        return string.Join(" ", list);
    }

    private string? FormatSymbolId(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId)) return null;
        if (_formatSymbolId != null) return _formatSymbolId(rawId);

        try
        {
            return HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(rawId);
        }
        catch
        {
            return rawId;
        }
    }

    private static string? TryCreateEnumFieldId(INamedTypeSymbol? enumSymbol, string fieldName)
    {
        if (enumSymbol is null) return null;
        var field = enumSymbol.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.Name == fieldName);
        return field?.GetDocumentationCommentId();
    }

    private static string? TryCreateRecordParameterId(INamedTypeSymbol? recordSymbol, string parameterName)
    {
        if (recordSymbol is null) return null;
        var prop = recordSymbol.GetMembers().OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.Name == parameterName);
        return prop?.GetDocumentationCommentId();
    }

    private static string NormalizeWhitespace(string input) =>
        WhitespaceRegex.Replace(input.Trim(), " ");
}

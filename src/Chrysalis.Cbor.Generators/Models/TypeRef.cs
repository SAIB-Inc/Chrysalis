using Microsoft.CodeAnalysis;

namespace Chrysalis.Cbor.Generators.Models;

public sealed class TypeRef(ITypeSymbol symbol)
{
    public string Name { get; } = symbol.Name;
    public string FullyQualifiedName { get; } = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    public string Namespace => symbol.ContainingNamespace.ToDisplayString();
    public bool IsValueType { get; } = symbol.IsValueType;
    public bool CanBeNull => !IsValueType;
}
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Chrysalis.Cbor.Generators.Utils;

/// <summary>
/// Helper for resolving union types during source generation
/// </summary>
internal static class UnionTypeResolver
{
    /// <summary>
    /// Resolves all union types for all types in the compilation that use UnionConverter
    /// </summary>
    /// <param name="types">All CBOR types collected from the compilation</param>
    /// <param name="compilation">The compilation context</param>
    /// <returns>Dictionary mapping type to its union types</returns>
    public static Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> ResolveAllUnionTypes(
        ImmutableArray<TypeDeclarationSyntax> types,
        Compilation compilation)
    {
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> result = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        List<INamedTypeSymbol> allTypeSymbols = [];

        // First, get all type symbols for easier processing
        foreach (TypeDeclarationSyntax typeSyntax in types)
        {
            SemanticModel model = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            if (model.GetDeclaredSymbol(typeSyntax) is INamedTypeSymbol symbol)
            {
                allTypeSymbols.Add(symbol);
            }
        }

        // Find types that use UnionConverter
        foreach (INamedTypeSymbol typeSymbol in allTypeSymbols)
        {
            AttributeData? converterAttr = typeSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "CborConverterAttribute");

            if (converterAttr == null || converterAttr.ConstructorArguments.Length == 0)
                continue;

            if (converterAttr.ConstructorArguments[0].Value is not INamedTypeSymbol converterTypeSymbol)
                continue;

            // Check if it's a UnionConverter
            if (converterTypeSymbol.Name != "UnionConverter" &&
                converterTypeSymbol.ToDisplayString() != "Chrysalis.Cbor.Serialization.Converters.Custom.UnionConverter")
                continue;

            // Found a type using UnionConverter, find all derived types
            List<INamedTypeSymbol> unionTypes = FindDerivedTypes(typeSymbol, allTypeSymbols);

            if (unionTypes.Count > 0)
            {
                result[typeSymbol] = unionTypes;
            }
        }

        return result;
    }

    /// <summary>
    /// Finds all types that derive from the given base type
    /// </summary>
    private static List<INamedTypeSymbol> FindDerivedTypes(
        INamedTypeSymbol baseType,
        List<INamedTypeSymbol> allTypes)
    {
        List<INamedTypeSymbol> result = [];

        if (baseType.IsGenericType)
        {
            // For generic base types
            INamedTypeSymbol baseGenericType = baseType.ConstructUnboundGenericType();

            foreach (INamedTypeSymbol type in allTypes)
            {
                // Skip if same type, abstract, or not generic
                if (SymbolEqualityComparer.Default.Equals(type, baseType) ||
                    type.IsAbstract ||
                    !type.IsGenericType)
                    continue;

                // Check if this type derives from the base generic type
                if (IsGenericSubclassOf(type, baseGenericType))
                {
                    result.Add(type);
                }
            }
        }
        else
        {
            // For non-generic base types
            foreach (INamedTypeSymbol type in allTypes)
            {
                // Skip if same type or abstract
                if (SymbolEqualityComparer.Default.Equals(type, baseType) ||
                    type.IsAbstract)
                    continue;

                // Check if this type derives from the base type
                if (InheritsFrom(type, baseType))
                {
                    result.Add(type);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if a type inherits from another type
    /// </summary>
    private static bool InheritsFrom(ITypeSymbol type, ITypeSymbol baseType)
    {
        INamedTypeSymbol? current = type.BaseType;

        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Checks if a generic type inherits from another generic type
    /// </summary>
    private static bool IsGenericSubclassOf(INamedTypeSymbol genericType, INamedTypeSymbol baseGenericType)
    {
        INamedTypeSymbol? current = genericType;

        while (current != null && !SymbolEqualityComparer.Default.Equals(current, current.ContainingAssembly.GetTypeByMetadataName("System.Object")))
        {
            if (current.IsGenericType)
            {
                INamedTypeSymbol currentOpenType = current.ConstructUnboundGenericType();
                if (SymbolEqualityComparer.Default.Equals(currentOpenType, baseGenericType))
                    return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Gets string representations of union types for a given type
    /// </summary>
    public static List<string> GetUnionTypeNames(
        INamedTypeSymbol typeSymbol,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> allUnionTypes)
    {
        if (allUnionTypes.TryGetValue(typeSymbol, out List<INamedTypeSymbol>? unionTypes))
        {
            return unionTypes.Select(t => t.ToDisplayString()).ToList();
        }

        return [];
    }
}
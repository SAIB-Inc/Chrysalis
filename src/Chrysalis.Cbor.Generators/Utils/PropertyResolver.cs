
using Microsoft.CodeAnalysis;

namespace Chrysalis.Cbor.Generators.Utils;

internal static class PropertyResolver
{
    // Helper method to resolve property mappings for a type
    public static (
        Dictionary<int, (string Type, string Name, string? DefaultValue)> IndexMap,
        Dictionary<string, (string Type, string Name, string? DefaultValue)> NamedMap,
        IMethodSymbol? Constructor
    ) ResolvePropertyMappings(INamedTypeSymbol typeSymbol)
    {
        Dictionary<int, (string Type, string Name, string? DefaultValue)> indexMap = new Dictionary<int, (string Type, string Name, string? DefaultValue)>();
        Dictionary<string, (string Type, string Name, string? DefaultValue)> namedMap = new Dictionary<string, (string Type, string Name, string? DefaultValue)>();
        IMethodSymbol? constructor = null;

        // Skip abstract types
        if (typeSymbol.IsAbstract)
            return (indexMap, namedMap, constructor);

        // Find the constructor with the most parameters
        constructor = typeSymbol.Constructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (constructor != null)
        {
            // Process each parameter
            foreach (IParameterSymbol param in constructor.Parameters)
            {
                // Look for index attribute
                AttributeData? indexAttr = param.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "CborIndexAttribute" ||
                                     a.AttributeClass?.ToDisplayString() == "Chrysalis.Cbor.Attributes.CborIndexAttribute");

                // Look for property attribute
                AttributeData? propAttr = param.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "CborPropertyAttribute" ||
                                     a.AttributeClass?.ToDisplayString() == "Chrysalis.Cbor.Attributes.CborPropertyAttribute");

                // Look for exact value attribute
                AttributeData? exactAttr = param.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "ExactValueAttribute" ||
                                     a.AttributeClass?.ToDisplayString() == "Chrysalis.Cbor.Attributes.ExactValueAttribute");

                string paramType = param.Type.ToDisplayString();
                string paramName = param.Name;
                string? defaultValue = null;

                // Try to extract default value
                if (exactAttr != null && exactAttr.ConstructorArguments.Length > 0)
                {
                    object? value = exactAttr.ConstructorArguments[0].Value;
                    defaultValue = FormatDefaultValue(value, param.Type);
                }

                // Map by index if present
                if (indexAttr != null && indexAttr.ConstructorArguments.Length > 0 &&
                    indexAttr.ConstructorArguments[0].Value is int index)
                {
                    indexMap[index] = (paramType, paramName, defaultValue);
                    continue;
                }

                // Map by name if present
                if (propAttr != null && propAttr.ConstructorArguments.Length > 0 &&
                    propAttr.ConstructorArguments[0].Value is string name)
                {
                    namedMap[name] = (paramType, paramName, defaultValue);
                }
                else if (propAttr != null)
                {
                    // Use parameter name as property name if not specified
                    namedMap[paramName] = (paramType, paramName, defaultValue);
                }
            }
        }

        return (indexMap, namedMap, constructor);
    }

    // Helper method to format default values appropriately
    public static string? FormatDefaultValue(object? value, ITypeSymbol type)
    {
        if (value == null)
            return "null";

        // Handle different types of values
        if (type.SpecialType == SpecialType.System_String)
            return $"\"{value}\"";

        if (type.SpecialType == SpecialType.System_Boolean)
            return value.ToString()?.ToLowerInvariant();

        if (type.SpecialType == SpecialType.System_Int32 ||
            type.SpecialType == SpecialType.System_Int64 ||
            type.SpecialType == SpecialType.System_Double ||
            type.SpecialType == SpecialType.System_Single)
            return value.ToString();

        // For other types, just use string representation
        return value.ToString();
    }
}
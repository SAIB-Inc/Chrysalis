using Microsoft.CodeAnalysis;

namespace Chrysalis.Cbor.Generators.Models;

public sealed class PropertyGenerationSpec
{
    public string Name { get; }
    public TypeRef PropertyType { get; }
    public int? Order { get; }
    public int? Size { get; }  // For fixed-size byte arrays
    public string Key { get; } // The key from [CborProperty]

    public PropertyGenerationSpec(IPropertySymbol property)
    {
        Name = property.Name;
        PropertyType = new TypeRef(property.Type);
        Order = ExtractOrderAttribute(property);
        Size = ExtractSizeAttribute(property);
        Key = ExtractKey(property) ?? property.Name;
    }

    private int? ExtractOrderAttribute(IPropertySymbol property)
    {
        AttributeData? attr = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "CborOrderAttribute");
        return attr?.ConstructorArguments.FirstOrDefault().Value as int?;
    }

    private int? ExtractSizeAttribute(IPropertySymbol property)
    {
        AttributeData? attr = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "CborSizeAttribute");
        return attr?.ConstructorArguments.FirstOrDefault().Value as int?;
    }

    private string? ExtractKey(IPropertySymbol property)
    {
        // Check both the short name and the full name in case of namespace differences.
        var attr = property.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == "CborPropertyAttribute" ||
            (a.AttributeClass?.ToDisplayString().Contains("CborPropertyAttribute") ?? false));

        if (attr != null && attr.ConstructorArguments.Length > 0)
        {
            return attr.ConstructorArguments[0].Value as string;
        }
        // Fallback: use the property name (but ideally the attribute is always present)
        return property.Name;
    }
}
using Microsoft.CodeAnalysis;

namespace Chrysalis.Cbor.SourceGenerator;

public sealed partial class CborSourceGenerator
{
    /// <summary>
    /// Reference to a type with essential type information
    /// </summary>
    private sealed class TypeInfo
    {
        public TypeInfo(ITypeSymbol symbol)
        {
            // Defensive null check
            if (symbol == null)
                throw new ArgumentNullException(nameof(symbol), "Type symbol cannot be null");

            Name = symbol.Name ?? string.Empty;

            // First check if this is a type parameter (like T) - needs special handling
            if (symbol.TypeKind == TypeKind.TypeParameter)
            {
                // For type parameters like T, keep the simple name
                FullName = Name;
                IsTypeParameter = true;
            }
            // Next check if it's a nested type (but not a type parameter)
            else if (symbol.ContainingType != null)
            {
                // Only format as nested if it's a real nested type, not a type parameter
                string containingTypeFullName = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                FullName = $"{containingTypeFullName}.{symbol.Name}";
                
                // Make sure we capture the correct namespace for nested types
                Namespace = symbol.ContainingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                IsTypeParameter = false;
            }
            else
            {
                // Regular type
                FullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? symbol.Name ?? string.Empty;
                IsTypeParameter = false;
            }

            Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            IsValueType = symbol.IsValueType;
            IsEnum = symbol.TypeKind == TypeKind.Enum;
            IsAbstract = symbol.IsAbstract;
            IsRecord = symbol.IsRecord;

            // Handle generic types
            IsGeneric = symbol is INamedTypeSymbol namedType && namedType.IsGenericType;

            if (IsGeneric && symbol is INamedTypeSymbol genericType)
            {
                // Store generic type parameters
                TypeParameters = [.. genericType.TypeArguments.Select(t => new TypeInfo(t))];
            }
            else
            {
                TypeParameters = [];
            }
        }

        // Add this property
        public bool IsTypeParameter { get; }
        public string Name { get; }
        public string FullName { get; }
        public string Namespace { get; }
        public bool IsValueType { get; }
        public bool IsEnum { get; }
        public bool IsAbstract { get; }
        public bool IsRecord { get; }
        public bool IsGeneric { get; }
        public List<TypeInfo> TypeParameters { get; }
        public bool CanBeNull => !IsValueType;
    }

    /// <summary>
    /// CBOR serialization strategies for different types
    /// </summary>
    private enum SerializationType
    {
        Object,     // Default object type
        Map,        // CBOR map
        Array,      // CBOR array/list
        Constr,     // Constructor-encoded type
        Union,      // Union type with multiple cases
        Nullable,   // Nullable type
        Container,   // Single-property container type
        Encoded
    }

    /// <summary>
    /// Serialization metadata for a CBOR type
    /// </summary>
    private sealed class SerializableType
    {
        public TypeInfo Type { get; set; } = null!;
        public string InfoPropertyName { get; set; } = string.Empty;
        public SerializationType Format { get; set; } = SerializationType.Object;

        // Type structure information
        public List<SerializableProperty> Properties { get; } = [];
        public List<ConstructorParam> Parameters { get; } = [];
        public HashSet<TypeInfo> Dependencies { get; } = [];

        // Collection info
        public TypeInfo? ElementType { get; set; }  // For collections
        public TypeInfo? KeyType { get; set; }      // For dictionaries

        // Special handling
        public int? Tag { get; set; }              // For tagged types
        public int? Constructor { get; set; }      // For constructor types
        public bool IsIndefinite { get; set; }     // For indefinite length 
        public int? Size { get; set; }             // For fixed size

        // Union handling
        public List<TypeInfo> UnionCases { get; } = [];

        // Nullable handling
        public TypeInfo? InnerType { get; set; }
        public SerializationType? InnerFormat { get; set; }

        // Validation
        public string? ValidatorTypeName { get; set; }
        public bool HasValidator => !string.IsNullOrEmpty(ValidatorTypeName);

        // Method hiding control (for determining when to use 'new' keyword)
        public bool HasBaseWriteMethod { get; set; }
        public bool HasBaseReadMethod { get; set; }
        public string DebugInfo { get; set; } = string.Empty;
    }

    /// <summary>
    /// Serialization metadata for a property
    /// </summary>
    private sealed class SerializableProperty
    {
        public SerializableProperty(IPropertySymbol property)
        {
            // Defensive null checks
            if (property == null)
                throw new ArgumentNullException(nameof(property), "Property symbol cannot be null");

            Name = property.Name ?? string.Empty;

            // Null check for property type
            if (property.Type == null)
            {
                Type = new TypeInfo(property.ContainingType); // Fallback to containing type
                IsCollection = false;
                IsDictionary = false;
                return;
            }
            else
            {
                Type = new TypeInfo(property.Type);
            }

            // Extract serialization attributes (with null checks)
            Key = ExtractKey(property) ?? property.Name ?? string.Empty;
            Order = ExtractOrderAttribute(property);
            Size = ExtractSizeAttribute(property);
            IsIndefinite = ExtractIndefiniteAttribute(property);
            IsCborNullable = ExtractCborNullableAttribute(property);
            IsPropertyNullable = Type.CanBeNull;

            // Analyze collection type (with null checks)
            if (property.Type is IArrayTypeSymbol arrayType && arrayType.ElementType != null)
            {
                IsCollection = true;
                ElementType = new TypeInfo(arrayType.ElementType);
            }
            else if (property.Type is INamedTypeSymbol namedType &&
                     namedType.ToDisplayString() != "string")
            {
                // Check for collections and dictionaries
                AnalyzeCollectionType(namedType);
            }
        }

        public string Name { get; }
        public TypeInfo Type { get; }

        // Serialization details
        public string? Key { get; }
        public int? Order { get; }
        public int? Size { get; }
        public bool IsIndefinite { get; }
        public bool IsCborNullable { get; }
        public bool IsPropertyNullable { get; }

        // Type structure
        public bool IsCollection { get; private set; }
        public bool IsDictionary { get; private set; }
        public TypeInfo? ElementType { get; private set; }
        public TypeInfo? KeyType { get; private set; }


        private void AnalyzeCollectionType(INamedTypeSymbol type)
        {
            // Check for dictionaries first
            INamedTypeSymbol? dictInterface = type.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>");

            if (dictInterface != null)
            {
                IsDictionary = true;
                IsCollection = true;
                KeyType = new TypeInfo(dictInterface.TypeArguments[0]);
                ElementType = new TypeInfo(dictInterface.TypeArguments[1]);
                return;
            }

            // Check for collections
            INamedTypeSymbol? enumInterface = type.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");

            if (enumInterface != null)
            {
                IsCollection = true;
                ElementType = new TypeInfo(enumInterface.TypeArguments[0]);
            }
        }

        // Attribute extraction methods
        private static int? ExtractOrderAttribute(IPropertySymbol property) =>
            property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == Constants.CborOrderAttribute)
                ?.ConstructorArguments.FirstOrDefault().Value as int?;

        private static int? ExtractSizeAttribute(IPropertySymbol property) =>
            property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == Constants.CborSizeAttribute)
                ?.ConstructorArguments.FirstOrDefault().Value as int?;

        private static string? ExtractKey(IPropertySymbol property)
        {
            var attr = property.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name == Constants.CborPropertyAttribute ||
                (a.AttributeClass?.ToDisplayString().Contains(Constants.CborPropertyAttribute) ?? false));

            return attr?.ConstructorArguments.FirstOrDefault().Value as string;
        }

        private static bool ExtractIndefiniteAttribute(IPropertySymbol property) =>
            property.GetAttributes().Any(a => a.AttributeClass?.Name == Constants.CborIndefiniteAttribute);

        private static bool ExtractCborNullableAttribute(IPropertySymbol property) =>
            property.GetAttributes().Any(a => a.AttributeClass?.Name == Constants.CborNullableAttribute);
    }

    private sealed class ConstructorParam(IParameterSymbol parameter)
    {
        public string Name { get; } = parameter.Name;
        public TypeInfo Type { get; } = new TypeInfo(parameter.Type);
        public int Position { get; } = parameter.Ordinal;
    }

    private sealed class SerializationContext
    {
        public TypeInfo ContextType { get; set; } = null!;
        public List<SerializableType> Types { get; set; } = [];
    }
}
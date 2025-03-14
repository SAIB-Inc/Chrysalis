using Chrysalis.Cbor.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.Generators;

public sealed partial class CborSourceGenerator
{
    private sealed class Parser()
    {
        private readonly Queue<TypeToProcess> _typesToProcess = new();
        private readonly Dictionary<ITypeSymbol, CborTypeGenerationSpec> _processedTypes =
            new(SymbolEqualityComparer.Default);


        public ContextGenerationSpec? ParseContextGenerationSpec(
            TypeDeclarationSyntax contextClass,
            SemanticModel semanticModel,
            CancellationToken cancellationToken
        )
        {
            // Ensure our internal caches are empty when starting a new context
            _typesToProcess.Clear();
            _processedTypes.Clear();

            // Convert the syntax node (contextClass) to a symbol we can analyze
            if (semanticModel.GetDeclaredSymbol(contextClass, cancellationToken) is not INamedTypeSymbol contextType)
            {
                return null;
            }

            // Find all types with relevant attributes
            List<TypeToProcess> rootTypes = [];

            foreach (AttributeData attribute in contextType.GetAttributes())
            {
                string? attrName = attribute.AttributeClass?.ToDisplayString();

                // Check for CborSerializable
                if (attrName == CborConstants.CborSerializableAttributeFullName)
                {
                    // If a type argument is provided, use it; otherwise, assume the context type itself.
                    ITypeSymbol typeToGenerate;
                    if (attribute.ConstructorArguments.Length > 0 &&
                        attribute.ConstructorArguments[0].Value is ITypeSymbol t)
                    {
                        typeToGenerate = t;
                    }
                    else
                    {
                        typeToGenerate = contextType;
                    }
                    rootTypes.Add(TypeToProcess.FromAttribute(typeToGenerate, attribute));
                }
                // Check for CborNullable
                else if (attrName == CborConstants.CborNullableAttributeFullName)
                {
                    // If a type argument is provided, use it; otherwise, assume the context type itself.
                    ITypeSymbol typeToGenerate;
                    if (attribute.ConstructorArguments.Length > 0 &&
                        attribute.ConstructorArguments[0].Value is ITypeSymbol t)
                    {
                        typeToGenerate = t;
                    }
                    else
                    {
                        typeToGenerate = contextType;
                    }
                    rootTypes.Add(TypeToProcess.FromAttribute(typeToGenerate, attribute, isNullable: true));
                }
            }

            if (rootTypes.Count == 0)
            {
                // No types were marked for serialization
                return null;
            }

            // Add all root types to the processing queue
            foreach (TypeToProcess rootType in rootTypes)
            {
                _typesToProcess.Enqueue(rootType);
            }

            // Process all types in the queue until none remain
            while (_typesToProcess.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TypeToProcess typeToProcess = _typesToProcess.Dequeue();

                // Skip if already processed
                if (_processedTypes.ContainsKey(typeToProcess.Type))
                {
                    continue;
                }

                CborTypeGenerationSpec spec = ParseTypeGenerationSpec(typeToProcess);

                // Add to the processed set
                _processedTypes.Add(typeToProcess.Type, spec);
            }


            // Create and return the context spec
            return new ContextGenerationSpec
            {
                ContextType = new TypeRef(contextType),
                Types = [.. _processedTypes.Values]
            };
        }

        public CborTypeGenerationSpec ParseTypeGenerationSpec(TypeToProcess typeToProcess)
        {
            CborTypeGenerationSpec spec = new()
            {
                TypeRef = new TypeRef(typeToProcess.Type),
                TypeInfoPropertyName = typeToProcess.Type.Name,
                Category = typeToProcess.IsNullable ? CborTypeCategory.Nullable : CborTypeCategory.Object
            };

            // If this is a nullable type, set the inner type information
            if (typeToProcess.IsNullable && typeToProcess.InnerType != null)
            {
                spec.InnerTypeRef = new TypeRef(typeToProcess.InnerType);

                // Determine the inner type category (if we know it)
                if (typeToProcess.InnerTypeCategory.HasValue)
                {
                    spec.InnerTypeCategory = typeToProcess.InnerTypeCategory.Value;
                }
            }

            bool isCategorized = false;
            bool hasTag = false;
            int? tag = null;
            bool hasSerializableAttribute = false;

            System.Collections.Immutable.ImmutableArray<AttributeData> attributes = typeToProcess.Type.GetAttributes();
            foreach (AttributeData attr in attributes)
            {
                string? attrName = attr.AttributeClass?.ToDisplayString();
                switch (attrName)
                {
                    case CborConstants.CborSerializableAttributeFullName:
                        hasSerializableAttribute = true;
                        break;
                    case CborConstants.CborMapAttributeFullName:
                        spec.Category = CborTypeCategory.Map;
                        isCategorized = true;
                        break;
                    case CborConstants.CborListAttributeFullName:
                        spec.Category = CborTypeCategory.Array;
                        isCategorized = true;
                        break;
                    case CborConstants.CborUnionAttributeFullName:
                        spec.Category = CborTypeCategory.Union;
                        isCategorized = true;
                        break;
                    case CborConstants.CborConstrAttributeFullName:
                        spec.Category = CborTypeCategory.Constr;
                        isCategorized = true;
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            spec.Constructor = attr.ConstructorArguments[0].Value as int?;
                        }
                        break;
                    case CborConstants.CborTagAttributeFullName:
                        hasTag = true;
                        if (attr.ConstructorArguments.Length > 0)
                        {
                            spec.Tag = attr.ConstructorArguments[0].Value as int?;
                            tag = spec.Tag;
                        }
                        break;
                    case CborConstants.CborNullableAttributeFullName:
                        spec.Category = CborTypeCategory.Nullable;
                        isCategorized = true;
                        break;
                }
            }

            // Iterate over each member of the type
            foreach (ISymbol member in typeToProcess.Type.GetMembers())
            {
                if (member is IPropertySymbol property)
                {
                    // Skip compiler-generated properties (e.g. EqualityContract)
                    if (property.IsImplicitlyDeclared)
                        continue;

                    // Create a new PropertyGenerationSpec for the property
                    PropertyGenerationSpec propSpec = new(property);

                    // Add the property spec to the list in our type spec
                    spec.Properties.Add(propSpec);

                    // Record dependency (if the property type is a custom type)
                    spec.Dependencies.Add(new TypeRef(property.Type));
                }
            }

            // If it's only // [CborSerializable] with no other CBOR-specific attributes and has a single property,
            // we'll treat it as a "pass-through" type
            if (hasSerializableAttribute && !isCategorized && spec.Properties.Count == 1)
            {
                spec.Category = CborTypeCategory.Container;
                if (hasTag)
                {
                    spec.Tag = tag;
                }
                isCategorized = true;
            }

            if (typeToProcess.Type is INamedTypeSymbol namedType)
            {
                List<IMethodSymbol> constructors = [.. namedType.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public)];

                if (constructors.Any())
                {
                    // For records, you may choose the constructor with the most parameters.
                    IMethodSymbol selectedConstructor = constructors.OrderByDescending(c => c.Parameters.Length).First();
                    foreach (IParameterSymbol parameter in selectedConstructor.Parameters)
                    {
                        ParameterGenerationSpec paramSpec = new(parameter);
                        spec.ConstructorParameters.Add(paramSpec);
                    }
                }
            }

            // If the type is marked as a union, collect all its nested union cases.
            if (spec.Category == CborTypeCategory.Union && typeToProcess.Type is INamedTypeSymbol unionType)
            {
                // Get all nested types of this union type
                foreach (INamedTypeSymbol nested in unionType.GetTypeMembers())
                {
                    spec.UnionCases.Add(new TypeRef(nested));
                    spec.Dependencies.Add(new TypeRef(nested));
                }
            }

            if (typeToProcess.Type is IArrayTypeSymbol arraySymbol)
            {
                spec.ElementType = new TypeRef(arraySymbol.ElementType);
            }
            // If not an array, but a named type, check for collection interfaces
            else if (typeToProcess.Type is INamedTypeSymbol namedCollection)
            {
                // Avoid treating strings as collections (since string implements IEnumerable<char>)
                if (namedCollection.ToDisplayString() != "string")
                {
                    // Check for IDictionary<TKey, TValue>
                    INamedTypeSymbol? dictInterface = namedCollection.AllInterfaces.FirstOrDefault(i =>
                        i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>");
                    if (dictInterface != null)
                    {
                        ITypeSymbol keyType = dictInterface.TypeArguments[0];
                        ITypeSymbol valueType = dictInterface.TypeArguments[1];
                        spec.KeyType = new TypeRef(keyType);
                        spec.ElementType = new TypeRef(valueType);
                    }
                    else
                    {
                        // Check for IEnumerable<T> to get the element type.
                        INamedTypeSymbol? enumerableInterface = namedCollection.AllInterfaces.FirstOrDefault(i =>
                            i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");
                        if (enumerableInterface != null)
                        {
                            spec.ElementType = new TypeRef(enumerableInterface.TypeArguments[0]);
                        }
                    }
                }
            }

            return spec;
        }
    }

    private struct TypeToProcess
    {
        /// <summary>
        /// The type symbol to process
        /// </summary>
        public ITypeSymbol Type { get; set; }

        /// <summary>
        /// Source location of the type (for error reporting)
        /// </summary>
        public Location? Location { get; set; }

        /// <summary>
        /// Location of the attribute that triggered this type's processing
        /// (useful for showing errors in the correct place)
        /// </summary>
        public Location? AttributeLocation { get; set; }

        /// <summary>
        /// Whether this type is nullable (marked with CborNullable)
        /// </summary>
        public bool IsNullable { get; set; }

        /// <summary>
        /// For nullable types, the inner type (what type is nullable)
        /// </summary>
        public ITypeSymbol? InnerType { get; set; }

        /// <summary>
        /// For nullable types, the category of the inner type (if known)
        /// </summary>
        public CborTypeCategory? InnerTypeCategory { get; set; }

        /// <summary>
        /// Creates a TypeToProcess from a type symbol
        /// </summary>
        public static TypeToProcess FromType(ITypeSymbol type)
        {
            return new TypeToProcess
            {
                Type = type,
                Location = type.Locations.FirstOrDefault()
            };
        }

        /// <summary>
        /// Creates a TypeToProcess from an attribute application
        /// </summary>
        public static TypeToProcess FromAttribute(ITypeSymbol type, AttributeData attribute,
            bool isNullable = false)
        {
            var result = new TypeToProcess
            {
                Type = type,
                Location = type.Locations.FirstOrDefault(),
                AttributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax()?.GetLocation(),
                IsNullable = isNullable
            };

            // If this is a nullable type and there's an argument specifying the inner type
            if (isNullable && attribute.ConstructorArguments.Length > 1 &&
                attribute.ConstructorArguments[1].Value is ITypeSymbol innerType)
            {
                result.InnerType = innerType;

                // Try to determine the inner type category from its attributes
                foreach (var innerAttr in innerType.GetAttributes())
                {
                    string? attrName = innerAttr.AttributeClass?.ToDisplayString();
                    switch (attrName)
                    {
                        case CborConstants.CborMapAttributeFullName:
                            result.InnerTypeCategory = CborTypeCategory.Map;
                            break;
                        case CborConstants.CborListAttributeFullName:
                            result.InnerTypeCategory = CborTypeCategory.Array;
                            break;
                        case CborConstants.CborUnionAttributeFullName:
                            result.InnerTypeCategory = CborTypeCategory.Union;
                            break;
                        case CborConstants.CborConstrAttributeFullName:
                            result.InnerTypeCategory = CborTypeCategory.Constr;
                            break;
                    }
                }
            }

            return result;
        }
    }
}
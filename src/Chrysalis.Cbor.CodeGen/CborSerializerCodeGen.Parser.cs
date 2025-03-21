using Chrysalis.Cbor.CodeGen.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.CodeGen;


public sealed partial class CborSerializerCodeGen
{
    private static class Parser
    {
        // fully qualified name -> SerializableTypeMetadata cache
        public static readonly Dictionary<string, SerializableTypeMetadata> _cache = [];

        public const string CborSerializableAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborSerializableAttribute";
        public const string CborSerializableAttribute = "CborSerializableAttribute";
        public const string CborSerializable = "CborSerializable";
        public const string CborMapAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborMapAttribute";
        public const string CborMapAttribute = "CborMapAttribute";
        public const string CborMap = "CborMap";
        public const string CborNullableAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborNullableAttribute";
        public const string CborNullableAttribute = "CborNullableAttribute";
        public const string CborNullable = "CborNullable";
        public const string CborListAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborListAttribute";
        public const string CborListAttribute = "CborListAttribute";
        public const string CborList = "CborList";
        public const string CborUnionAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborUnionAttribute";
        public const string CborUnionAttribute = "CborUnionAttribute";
        public const string CborUnion = "CborUnion";
        public const string CborConstrAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborConstrAttribute";
        public const string CborConstrAttribute = "CborConstrAttribute";
        public const string CborConstr = "CborConstr";
        public const string CborTagAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborTagAttribute";
        public const string CborTagAttribute = "CborTagAttribute";
        public const string CborTag = "CborTag";
        public const string CborOrderAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborOrderAttribute";
        public const string CborOrderAttribute = "CborOrderAttribute";
        public const string CborOrder = "CborOrder";
        public const string CborPropertyAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborPropertyAttribute";
        public const string CborPropertyAttribute = "CborPropertyAttribute";
        public const string CborProperty = "CborProperty";
        public const string CborSizeAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborSizeAttribute";
        public const string CborSizeAttribute = "CborSizeAttribute";
        public const string CborSize = "CborSize";
        public const string CborIndefiniteAttributeFullName = "Chrysalis.Cbor.Serialization.Attributes.CborIndefiniteAttribute";
        public const string CborIndefiniteAttribute = "CborIndefiniteAttribute";
        public const string CborIndefinite = "CborIndefinite";

        // Interfaces
        public const string ICborPreserveRawFullName = "Chrysalis.Cbor.Serialization.ICborPreserveRaw";
        public const string ICborPreserveRaw = "ICborPreserveRaw";
        public const string ICborValidatorFullName = "Chrysalis.Cbor.Serialization.ICborValidator`1";
        public const string ICborValidator = "ICborValidator`1";

        public static SerializableTypeMetadata? ParseSerialazableType(TypeDeclarationSyntax tds, SemanticModel model)
        {
            IEnumerable<AttributeSyntax> attributes = tds.AttributeLists.SelectMany(a => a.Attributes);
            AttributeSyntax? cborSerializableAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborSerializable);

            if (cborSerializableAttribute == null) return null;

            // Get the containing namespace (without parent types)
            string baseNamespace = GetBaseNamespace(tds, model);

            // Get the full namespace with parent types for the fully qualified name
            (string path, string parentTypeParams) = GetFullNamespaceWithParentsAndTypeParams(tds, model);

            // Get the keyword (class, record, struct, etc.)
            string? keyword = tds.Keyword.ToFullString();

            // Set the namespace to just the base namespace
            string? @namespace = baseNamespace;

            // Get the type parameters if any
            string? typeParams = null;
            if (tds is TypeDeclarationSyntax typeDecl && typeDecl.TypeParameterList != null)
            {
                typeParams = typeDecl.TypeParameterList.ToString();
            }

            typeParams ??= parentTypeParams;

            string baseIdentifier = RemoveGenericPart(tds.Identifier.ToFullString());
            string identifier = $"{baseIdentifier}{typeParams}";

            // For the fully qualified name, use the full path including parent types
            string fullyQualifiedName = string.IsNullOrEmpty(path) ? baseIdentifier : $"{path}.{baseIdentifier}";

            if (string.IsNullOrEmpty(parentTypeParams))
            {
                fullyQualifiedName = $"{fullyQualifiedName}{typeParams}";
            }

            // Extract type attributes
            AttributeSyntax? cborMapAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborMap);
            AttributeSyntax? cborListAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborList);
            AttributeSyntax? cborUnionAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborUnion);
            AttributeSyntax? cborConstrAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborConstr);
            AttributeSyntax? cborTagAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborTag);

            int? constrIndex = cborConstrAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;
            int? cborTag = cborTagAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;
            bool shouldPreserveRaw = ShouldPreserveRaw(tds, model);

            SerializationType serializationType = GetSerializationType(cborMapAttribute, cborListAttribute, cborUnionAttribute, cborConstrAttribute);
            string? validatorFullyQualifiedName = FindValidatorForType(tds, model, model.Compilation);

            IEnumerable<SerializablePropertyMetadata> properties = GetPropertyMetadata(tds, model);
            IEnumerable<SerializableTypeMetadata> childTypes = [];

            SerializableTypeMetadata typeMetadata = new(
                baseIdentifier,
                identifier,
                @namespace,
                typeParams,
                fullyQualifiedName,
                keyword,
                cborTag,
                constrIndex,
                serializationType,
                validatorFullyQualifiedName,
                shouldPreserveRaw
            );

            if (serializationType == SerializationType.Union)
            {
                childTypes = GetChildTypes(tds, model);
            }

            typeMetadata.Properties.AddRange(properties);
            typeMetadata.ChildTypes.AddRange(childTypes);

            _cache[typeMetadata.FullyQualifiedName.Replace("global::", "")] = typeMetadata;

            return typeMetadata;
        }
    }

    public static bool ShouldPreserveRaw(TypeDeclarationSyntax tds, SemanticModel semanticModel)
    {
        // Get type symbol with null check and explicit cast
        if (semanticModel.GetDeclaredSymbol(tds) is not INamedTypeSymbol typeSymbol)
            return false;

        // Approach 1: Get interface by metadata name (most reliable)
        INamedTypeSymbol? interfaceSymbol = semanticModel.Compilation.GetTypeByMetadataName(Parser.ICborPreserveRaw);
        if (interfaceSymbol != null)
            return typeSymbol.AllInterfaces.Contains(interfaceSymbol);

        // Approach 2: Try different name formats
        return typeSymbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == Parser.ICborPreserveRaw ||
            i.ToDisplayString() == Parser.ICborPreserveRawFullName ||
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"global::{Parser.ICborPreserveRawFullName}");
    }

    private static SerializationType GetSerializationType(AttributeSyntax? cborMapAttribute, AttributeSyntax? cborListAttribute, AttributeSyntax? cborUnionAttribute, AttributeSyntax? cborConstrAttribute)
    {
        if (cborMapAttribute != null) return SerializationType.Map;
        if (cborListAttribute != null) return SerializationType.List;
        if (cborUnionAttribute != null) return SerializationType.Union;
        if (cborConstrAttribute != null) return SerializationType.Constr;

        return SerializationType.Container;
    }

    private static string? FindValidatorForType(TypeDeclarationSyntax typeDecl, SemanticModel semanticModel, Compilation compilation)
    {
        if (semanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol typeSymbol) return null;

        INamedTypeSymbol? openValidatorInterface = compilation.GetTypeByMetadataName(Parser.ICborValidatorFullName);
        if (openValidatorInterface == null) return null;

        INamedTypeSymbol closedValidatorInterface = openValidatorInterface.Construct(typeSymbol);
        INamedTypeSymbol validator = compilation
            .GetSymbolsWithName(_ => true, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(t => t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, closedValidatorInterface)));

        return validator?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static IEnumerable<SerializablePropertyMetadata> GetPropertyMetadata(TypeDeclarationSyntax tds, SemanticModel model)
    {
        if (tds is ClassDeclarationSyntax classDecl)
        {
            return GetClassPropertyMetadata(classDecl, model);
        }
        else if (tds is RecordDeclarationSyntax recordDecl)
        {
            return GetRecordPropertyMetadata(recordDecl, model);
        }

        return [];
    }

    private static IEnumerable<SerializablePropertyMetadata> GetClassPropertyMetadata(ClassDeclarationSyntax classDecl, SemanticModel model)
    {
        IEnumerable<AttributeSyntax> attributes = classDecl.AttributeLists.SelectMany(a => a.Attributes);
        IEnumerable<PropertyDeclarationSyntax> properties = classDecl.DescendantNodes().OfType<PropertyDeclarationSyntax>();

        foreach (PropertyDeclarationSyntax property in properties)
        {
            string propertyName = property.Identifier.ToFullString();
            string propertyType = property.Type.ToFullString();
            string propertyTypeFullName = model.GetSymbolInfo(property.Type).Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
            string propertyTypeNamespace = model.GetSymbolInfo(property.Type).Symbol?.ContainingNamespace.ToDisplayString() ?? string.Empty;
            bool isNullable = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborNullable) is not null;
            bool isIndefinite = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborIndefinite) is not null;
            int? size = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborSize)?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;
            int? order = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborOrder)?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;
            GetCborPropertyKey(property, model, out string? stringKey, out int? intKey);


            yield return new SerializablePropertyMetadata(
                propertyName,
                propertyType,
                propertyTypeFullName,
                propertyTypeNamespace,
                false,
                false,
                null,
                null,
                null,
                false,
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                isNullable,
                size,
                isIndefinite,
                order,
                stringKey,
                intKey,
                false
            );
        }
    }

    private static IEnumerable<SerializablePropertyMetadata> GetRecordPropertyMetadata(RecordDeclarationSyntax recordDecl, SemanticModel model)
    {
        foreach (ParameterSyntax param in recordDecl.ParameterList?.Parameters ?? [])
        {
            IEnumerable<AttributeSyntax> paramAttributes = param.AttributeLists.SelectMany(a => a.Attributes);

            string propertyName = param.Identifier.Text;
            string propertyType = param.Type?.ToString() ?? throw new ArgumentNullException(nameof(param.Type));
            string propertyTypeNamespace = string.Empty;
            bool isOpenGeneric = false;
            ISymbol? typeSymbol = model.GetSymbolInfo(param.Type).Symbol;
            string propertyTypeFullName = "";

            if (typeSymbol is ITypeSymbol its)
            {
                if (its.TypeKind is TypeKind.TypeParameter)
                {
                    isOpenGeneric = true;
                }

                propertyTypeFullName = its.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            if (typeSymbol != null)
            {
                // Handle primitives and special types
                if (typeSymbol is INamedTypeSymbol namedType &&
                    namedType.ContainingNamespace != null &&
                    !namedType.ContainingNamespace.IsGlobalNamespace)
                {
                    propertyTypeNamespace = namedType.ContainingNamespace.ToDisplayString();
                }
                else if (typeSymbol is IArrayTypeSymbol arrayType)
                {
                    // For arrays, get the element type's namespace
                    ITypeSymbol elementType = arrayType.ElementType;
                    if (elementType.ContainingNamespace != null &&
                        !elementType.ContainingNamespace.IsGlobalNamespace)
                    {
                        propertyTypeNamespace = elementType.ContainingNamespace.ToDisplayString();
                    }
                }
            }

            bool isNullable = paramAttributes.Any(a => a.Name.ToString() == Parser.CborNullable);
            bool isIndefinite = paramAttributes.Any(a => a.Name.ToString() == Parser.CborIndefinite);

            int? size = null;
            AttributeSyntax sizeAttr = paramAttributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborSize);
            if (sizeAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax sizeArg)
            {
                Optional<object?> constValue = model.GetConstantValue(sizeArg.Expression);
                if (constValue.HasValue && constValue.Value is int sizeVal)
                    size = sizeVal;
            }

            int? order = null;
            AttributeSyntax orderAttr = paramAttributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborOrder);
            if (orderAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax orderArg)
            {
                Optional<object?> constValue = model.GetConstantValue(orderArg.Expression);
                if (constValue.HasValue && constValue.Value is int orderVal)
                    order = orderVal;
            }

            GetCborPropertyKey(param, model, out string? stringKey, out int? intKey);

            bool isList = false;
            bool isMap = false;

            string? listItemType = null;
            string? listItemTypeFullName = null;
            string? listItemTypeNamespace = null;
            bool isListItemTypeOpenGeneric = false;


            bool isMapKeyTypeOpenGeneric = false;
            bool isMapValueTypeOpenGeneric = false;
            string? mapKeyType = null;
            string? mapValueType = null;
            string? mapKeyTypeFullName = null;
            string? mapValueTypeFullName = null;
            string? mapKeyTypeNamespace = null;
            string? mapValueTypeNamespace = null;

            if (typeSymbol is ITypeSymbol ts)
            {

                if (IsMapType(ts, out ITypeSymbol? keyTypeSymbol, out ITypeSymbol? valueTypeSymbol))
                {
                    isMap = true;
                    mapKeyType = keyTypeSymbol?.Name;
                    mapKeyTypeFullName = keyTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    mapValueType = valueTypeSymbol?.Name;
                    mapValueTypeFullName = valueTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    mapKeyTypeNamespace = keyTypeSymbol?.ContainingNamespace?.IsGlobalNamespace == false ?
                        keyTypeSymbol.ContainingNamespace.ToDisplayString() : null;
                    mapValueTypeNamespace = valueTypeSymbol?.ContainingNamespace?.IsGlobalNamespace == false ?
                        valueTypeSymbol.ContainingNamespace.ToDisplayString() : null;

                    isMapKeyTypeOpenGeneric = (keyTypeSymbol?.TypeKind == TypeKind.TypeParameter) ||
                        (keyTypeSymbol is INamedTypeSymbol namedKeyType &&
                            namedKeyType.IsGenericType &&
                            namedKeyType.TypeArguments.Any(ta => ta.TypeKind == TypeKind.TypeParameter));

                    isMapValueTypeOpenGeneric = (valueTypeSymbol?.TypeKind == TypeKind.TypeParameter) ||
                        (valueTypeSymbol is INamedTypeSymbol namedValueType &&
                            namedValueType.IsGenericType &&
                            namedValueType.TypeArguments.Any(ta => ta.TypeKind == TypeKind.TypeParameter));

                    mapKeyTypeFullName = keyTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    mapValueTypeFullName = valueTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                }
                else if (IsListType(ts, out ITypeSymbol? itemTypeSymbol))
                {
                    isList = true;
                    listItemType = itemTypeSymbol?.Name;
                    listItemTypeFullName = itemTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    listItemTypeNamespace = itemTypeSymbol?.ContainingNamespace?.IsGlobalNamespace == false ?
                        itemTypeSymbol.ContainingNamespace.ToDisplayString() : null;
                    isListItemTypeOpenGeneric = (itemTypeSymbol?.TypeKind == TypeKind.TypeParameter) ||
                        (itemTypeSymbol is INamedTypeSymbol namedItemType &&
                            namedItemType.IsGenericType &&
                            namedItemType.TypeArguments.Any(ta => ta.TypeKind == TypeKind.TypeParameter));
                }
            }

            yield return new SerializablePropertyMetadata(
                propertyName,
                propertyType,
                propertyTypeFullName,
                propertyTypeNamespace,
                isList,
                isMap,
                listItemType,
                listItemTypeFullName,
                listItemTypeNamespace,
                isListItemTypeOpenGeneric,
                isMapKeyTypeOpenGeneric,
                isMapValueTypeOpenGeneric,
                mapKeyType,
                mapValueType,
                mapKeyTypeFullName,
                mapValueTypeFullName,
                mapKeyTypeNamespace,
                mapValueTypeNamespace,
                isNullable,
                size,
                isIndefinite,
                order,
                stringKey,
                intKey,
                isOpenGeneric
            );
        }
    }

    private static void GetCborPropertyKey(SyntaxNode node, SemanticModel semanticModel, out string? stringKey, out int? intKey)
    {
        stringKey = null;
        intKey = null;

        ISymbol? symbol = semanticModel.GetDeclaredSymbol(node);
        AttributeData? attribute = symbol?.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == Parser.CborProperty ||
                              a.AttributeClass?.Name == Parser.CborPropertyAttribute);

        if (attribute?.ConstructorArguments.FirstOrDefault() is TypedConstant arg)
        {
            if (arg.Kind == TypedConstantKind.Primitive && arg.Value is int intValue)
            {
                intKey = intValue;
            }
            else if (arg.Kind == TypedConstantKind.Primitive && arg.Value is string stringValue)
            {
                stringKey = stringValue;
            }
        }
    }

    private static IEnumerable<SerializableTypeMetadata> GetChildTypes(TypeDeclarationSyntax tds, SemanticModel model)
    {
        // Get the type symbol for the current declaration
        if (model.GetDeclaredSymbol(tds) is not INamedTypeSymbol typeSymbol)
            return Enumerable.Empty<SerializableTypeMetadata>();

        // Get all derived types across the entire compilation
        IEnumerable<INamedTypeSymbol> derivedTypes = FindDerivedTypes(typeSymbol, model.Compilation);

        // Process both nested types and derived types
        List<SerializableTypeMetadata> result = new();

        // Add nested types (original behavior)
        foreach (TypeDeclarationSyntax nestedType in tds.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (Parser.ParseSerialazableType(nestedType, model) is SerializableTypeMetadata metadata)
                result.Add(metadata);
        }

        // Add derived types (new behavior)
        foreach (INamedTypeSymbol derivedType in derivedTypes)
        {
            // Find the syntax reference for the derived type
            SyntaxReference? syntaxRef = derivedType.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef?.GetSyntax() is TypeDeclarationSyntax derivedTypeSyntax)
            {
                // Create a new semantic model for the derived type's syntax tree if needed
                SemanticModel derivedTypeModel = model;
                if (syntaxRef.SyntaxTree != model.SyntaxTree)
                    derivedTypeModel = model.Compilation.GetSemanticModel(syntaxRef.SyntaxTree);

                if (Parser.ParseSerialazableType(derivedTypeSyntax, derivedTypeModel) is SerializableTypeMetadata metadata)
                    result.Add(metadata);
            }
        }

        return result;
    }

    private static IEnumerable<INamedTypeSymbol> FindDerivedTypes(INamedTypeSymbol baseType, Compilation compilation)
    {
        // Find all types in the compilation that inherit from the given type
        return compilation.GetSymbolsWithName(
            name => true,  // Consider all names
            SymbolFilter.Type
        )
        .OfType<INamedTypeSymbol>()
        .Where(type =>
            !SymbolEqualityComparer.Default.Equals(type, baseType) && // Exclude the base type itself
            type.BaseType != null &&                                  // Must have a base type
            ImplementsOrInheritsFrom(type, baseType)                  // Must implement or inherit from base type
        );
    }

    private static bool ImplementsOrInheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        // Check if it's a direct inheritance relationship
        if (baseType.TypeKind == TypeKind.Interface)
        {
            // For interfaces, check if the type implements the interface
            return type.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, baseType.OriginalDefinition));
        }
        else
        {
            // For classes or records, walk up the inheritance chain
            INamedTypeSymbol? currentType = type;
            while (currentType != null)
            {
                if (SymbolEqualityComparer.Default.Equals(currentType.OriginalDefinition, baseType.OriginalDefinition))
                    return true;

                currentType = currentType.BaseType;
            }
        }

        return false;
    }

    // Helper method to detect if a type is a list (implements IEnumerable<T>)
    private static bool IsListType(ITypeSymbol? typeSymbol, out ITypeSymbol? itemType)
    {
        itemType = null;

        // Check if it's an array (special case)
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            itemType = arrayType.ElementType;
            return true;
        }

        // Check if it implements IEnumerable<T> but isn't a string
        if (typeSymbol is INamedTypeSymbol namedType && namedType.SpecialType != SpecialType.System_String)
        {
            foreach (INamedTypeSymbol iface in namedType.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                {
                    itemType = iface.TypeArguments[0];
                    return true;
                }
            }
        }

        return false;
    }

    // Helper method to detect if a type is a map (implements IDictionary<K,V>)
    private static bool IsMapType(ITypeSymbol typeSymbol, out ITypeSymbol? keyType, out ITypeSymbol? valueType)
    {
        keyType = null;
        valueType = null;

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            foreach (INamedTypeSymbol iface in namedType.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>")
                {
                    keyType = iface.TypeArguments[0];
                    valueType = iface.TypeArguments[1];
                    return true;
                }
            }
        }

        return false;
    }

    private static (string path, string typeParams) GetFullNamespaceWithParentsAndTypeParams(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        // Get the declared symbol for most accurate information
        if (model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
        {
            // For nested types, the ContainingType will be non-null
            if (symbol.ContainingType != null)
            {
                string containerName = symbol.ContainingType.ToDisplayString();

                // Extract the type parameters from the containing type
                string parentTypeParams = "";
                if (symbol.ContainingType.TypeParameters.Length > 0)
                {
                    parentTypeParams = "<" + string.Join(", ", symbol.ContainingType.TypeParameters.Select(tp => tp.Name)) + ">";
                }

                return (containerName, parentTypeParams);
            }

            // For non-nested types, use the containing namespace
            return (symbol.ContainingNamespace.ToDisplayString(), "");
        }

        // Fallback implementation...
        return (typeDecl.GetNamespace() ?? string.Empty, "");
    }
    private static string GetBaseNamespace(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        // Get the declared symbol for most accurate information
        if (model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
        {
            return symbol.ContainingNamespace.ToDisplayString();
        }

        // Fallback to syntax-based approach
        return typeDecl.GetNamespace() ?? string.Empty;
    }

    private static string RemoveGenericPart(string identifier)
    {
        int angleBracketIndex = identifier.IndexOf('<');
        if (angleBracketIndex > 0)
        {
            return identifier.Substring(0, angleBracketIndex).Trim();
        }
        return identifier.Trim();
    }
}
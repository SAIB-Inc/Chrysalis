using Chrysalis.Cbor.CodeGen.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Cbor.CodeGen;


public sealed partial class CborSerializerCodeGen
{
    private static class Parser
    {
        public static readonly Dictionary<string, SerializableTypeMetadata> _cache = [];

        public const string CborSerializable = "CborSerializable";
        public const string CborSerializableAttribute = "CborSerializableAttribute";
        public const string CborMap = "CborMap";
        public const string CborNullable = "CborNullable";
        public const string CborList = "CborList";
        public const string CborUnion = "CborUnion";
        public const string CborUnionAttribute = "CborUnionAttribute";
        public const string CborConstr = "CborConstr";
        public const string CborTag = "CborTag";
        public const string CborOrder = "CborOrder";
        public const string CborPropertyAttribute = "CborPropertyAttribute";
        public const string CborProperty = "CborProperty";
        public const string CborSize = "CborSize";
        public const string CborIndefinite = "CborIndefinite";
        public const string CborDefinite = "CborDefinite";
        public const string CborUnionHint = "CborUnionHint";
        public const string CborUnionHintAttribute = "CborUnionHintAttribute";

        // Interfaces
        public const string ICborPreserveRawFullName = "Chrysalis.Cbor.Serialization.ICborPreserveRaw";
        public const string ICborPreserveRaw = "ICborPreserveRaw";
        public const string ICborValidatorFullName = "Chrysalis.Cbor.Serialization.ICborValidator`1";

        public static SerializableTypeMetadata? ParseSerialazableType(TypeDeclarationSyntax tds, SemanticModel model)
        {
            List<AttributeSyntax> attributes = [.. tds.AttributeLists.SelectMany(a => a.Attributes)];
            AttributeSyntax? cborSerializableAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborSerializable);

            if (cborSerializableAttribute == null)
            {
                return null;
            }

            string baseNamespace = GetBaseNamespace(tds, model);
            (string path, string parentTypeParams) = GetFullNamespaceWithParentsAndTypeParams(tds, model);
            string? keyword = tds.Keyword.ToFullString();
            string? @namespace = baseNamespace;

            string? typeParams = null;
            if (tds is TypeDeclarationSyntax typeDecl && typeDecl.TypeParameterList != null)
            {
                typeParams = typeDecl.TypeParameterList.ToString();
            }

            typeParams ??= parentTypeParams;

            string baseIdentifier = RemoveGenericPart(tds.Identifier.ToFullString());
            string identifier = $"{baseIdentifier}{typeParams}";

            string fullyQualifiedName = string.IsNullOrEmpty(path) ? baseIdentifier : $"{path}.{baseIdentifier}";

            if (string.IsNullOrEmpty(parentTypeParams))
            {
                fullyQualifiedName = $"{fullyQualifiedName}{typeParams}";
            }

            AttributeSyntax? cborMapAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborMap);
            AttributeSyntax? cborListAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborList);
            AttributeSyntax? cborUnionAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborUnion);
            AttributeSyntax? cborConstrAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborConstr);
            AttributeSyntax? cborTagAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborTag);
            AttributeSyntax? cborIndefiniteAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborIndefinite);
            AttributeSyntax? cborDefiniteAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborDefinite);

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
                cborIndefiniteAttribute != null,
                cborDefiniteAttribute != null,
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

    /// <summary>
    /// Determines whether the given type declaration implements the ICborPreserveRaw interface.
    /// </summary>
    /// <param name="tds">The type declaration syntax node.</param>
    /// <param name="semanticModel">The semantic model for symbol resolution.</param>
    /// <returns>True if the type preserves raw CBOR data; otherwise false.</returns>
    public static bool ShouldPreserveRaw(TypeDeclarationSyntax tds, SemanticModel semanticModel)
    {
        if (semanticModel is null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        if (semanticModel.GetDeclaredSymbol(tds) is not INamedTypeSymbol typeSymbol)
        {
            return false;
        }

        INamedTypeSymbol? interfaceSymbol = semanticModel.Compilation.GetTypeByMetadataName(Parser.ICborPreserveRaw);
        return interfaceSymbol != null
            ? typeSymbol.AllInterfaces.Contains(interfaceSymbol)
            : typeSymbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == Parser.ICborPreserveRaw ||
            i.ToDisplayString() == Parser.ICborPreserveRawFullName ||
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"global::{Parser.ICborPreserveRawFullName}");
    }

    private static SerializationType GetSerializationType(AttributeSyntax? cborMapAttribute, AttributeSyntax? cborListAttribute, AttributeSyntax? cborUnionAttribute, AttributeSyntax? cborConstrAttribute)
    {
        return cborMapAttribute != null
            ? SerializationType.Map
            : cborListAttribute != null
                ? SerializationType.List
                : cborUnionAttribute != null
                    ? SerializationType.Union
                    : cborConstrAttribute != null ? SerializationType.Constr : SerializationType.Container;
    }

    private static string? FindValidatorForType(TypeDeclarationSyntax typeDecl, SemanticModel semanticModel, Compilation compilation)
    {
        if (semanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        INamedTypeSymbol? openValidatorInterface = compilation.GetTypeByMetadataName(Parser.ICborValidatorFullName);
        if (openValidatorInterface == null)
        {
            return null;
        }

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
        List<AttributeSyntax> attributes = [.. classDecl.AttributeLists.SelectMany(a => a.Attributes)];
        IEnumerable<PropertyDeclarationSyntax> properties = classDecl.DescendantNodes().OfType<PropertyDeclarationSyntax>();

        foreach (PropertyDeclarationSyntax property in properties)
        {
            string propertyName = property.Identifier.ToFullString();
            string propertyType = property.Type.ToFullString();
            ISymbol? propertyTypeSymbol = model.GetSymbolInfo(property.Type).Symbol;
            string propertyTypeFullName = propertyTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
            string propertyTypeNamespace = propertyTypeSymbol?.ContainingNamespace.ToDisplayString() ?? string.Empty;
            bool useReaderOverloadForType = propertyTypeSymbol is ITypeSymbol propertyTypeITypeSymbol &&
                                            CanUseReaderOverload(propertyTypeITypeSymbol, model.Compilation);
            bool isNullable = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborNullable) is not null;
            bool isIndefinite = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborIndefinite) is not null;
            bool isDefinite = attributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborDefinite) is not null;
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
                isDefinite,
                order,
                stringKey,
                intKey,
                false,
                useReaderOverloadForType,
                false,
                false,
                false
            );
        }
    }

    private static IEnumerable<SerializablePropertyMetadata> GetRecordPropertyMetadata(RecordDeclarationSyntax recordDecl, SemanticModel model)
    {
        foreach (ParameterSyntax param in recordDecl.ParameterList?.Parameters ?? [])
        {
            List<AttributeSyntax> paramAttributes = [.. param.AttributeLists.SelectMany(a => a.Attributes)];

            string propertyName = param.Identifier.Text;
            string propertyType = param.Type?.ToString() ?? throw new ArgumentNullException(nameof(recordDecl), "Parameter type cannot be null");
            string propertyTypeNamespace = string.Empty;
            bool isOpenGeneric = false;
            bool useReaderOverloadForType = false;
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
                if (typeSymbol is INamedTypeSymbol namedType &&
                    namedType.ContainingNamespace != null &&
                    !namedType.ContainingNamespace.IsGlobalNamespace)
                {
                    propertyTypeNamespace = namedType.ContainingNamespace.ToDisplayString();
                }
                else if (typeSymbol is IArrayTypeSymbol arrayType)
                {
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
            bool isDefinite = paramAttributes.Any(a => a.Name.ToString() == Parser.CborDefinite);

            int? size = null;
            AttributeSyntax sizeAttr = paramAttributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborSize);
            if (sizeAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax sizeArg)
            {
                Optional<object?> constValue = model.GetConstantValue(sizeArg.Expression);
                if (constValue.HasValue && constValue.Value is int sizeVal)
                {
                    size = sizeVal;
                }
            }

            int? order = null;
            AttributeSyntax orderAttr = paramAttributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborOrder);
            if (orderAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax orderArg)
            {
                Optional<object?> constValue = model.GetConstantValue(orderArg.Expression);
                if (constValue.HasValue && constValue.Value is int orderVal)
                {
                    order = orderVal;
                }
            }

            GetCborPropertyKey(param, model, out string? stringKey, out int? intKey);

            bool isList = false;
            bool isMap = false;
            bool useReaderOverloadForListItem = false;
            bool useReaderOverloadForMapKey = false;
            bool useReaderOverloadForMapValue = false;

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
                useReaderOverloadForType = CanUseReaderOverload(ts, model.Compilation);

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
                    useReaderOverloadForMapKey = keyTypeSymbol is not null && CanUseReaderOverload(keyTypeSymbol, model.Compilation);
                    useReaderOverloadForMapValue = valueTypeSymbol is not null && CanUseReaderOverload(valueTypeSymbol, model.Compilation);

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
                    useReaderOverloadForListItem = itemTypeSymbol is not null && CanUseReaderOverload(itemTypeSymbol, model.Compilation);
                }
            }

            SerializablePropertyMetadata propMetadata = new(
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
                isDefinite,
                order,
                stringKey,
                intKey,
                isOpenGeneric,
                useReaderOverloadForType,
                useReaderOverloadForListItem,
                useReaderOverloadForMapKey,
                useReaderOverloadForMapValue
            );

            // Parse [CborUnionHint] attributes
            ParseUnionHints(param, model, propMetadata);

            yield return propMetadata;
        }
    }

    private static void GetCborPropertyKey(SyntaxNode node, SemanticModel semanticModel, out string? stringKey, out int? intKey)
    {
        stringKey = null;
        intKey = null;

        ISymbol? symbol = semanticModel.GetDeclaredSymbol(node);
        AttributeData? attribute = symbol?.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is Parser.CborProperty or
                              Parser.CborPropertyAttribute);

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

    private static void ParseUnionHints(ParameterSyntax param, SemanticModel model, SerializablePropertyMetadata propMetadata)
    {
        ISymbol? symbol = model.GetDeclaredSymbol(param);
        if (symbol is null)
        {
            return;
        }

        AttributeData[] hintAttrs = [.. symbol.GetAttributes()
            .Where(a => a.AttributeClass?.Name is Parser.CborUnionHint or Parser.CborUnionHintAttribute)];

        if (hintAttrs.Length == 0)
        {
            return;
        }

        foreach (AttributeData attr in hintAttrs)
        {
            if (attr.ConstructorArguments.Length < 3)
            {
                continue;
            }

            TypedConstant discriminantPropArg = attr.ConstructorArguments[0];
            TypedConstant discriminantValueArg = attr.ConstructorArguments[1];
            TypedConstant typeArg = attr.ConstructorArguments[2];

            if (discriminantPropArg.Kind == TypedConstantKind.Primitive && discriminantPropArg.Value is string discriminantProp
                && discriminantValueArg.Kind == TypedConstantKind.Primitive && discriminantValueArg.Value is int discriminantValue
                && typeArg.Kind == TypedConstantKind.Type && typeArg.Value is INamedTypeSymbol concreteType)
            {
                string concreteTypeFqn = concreteType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                propMetadata.UnionHints[discriminantValue] = concreteTypeFqn;
                propMetadata.UnionHintDiscriminantProperty ??= discriminantProp;
            }
        }
    }

    private static Dictionary<string, SerializableTypeMetadata>.ValueCollection GetChildTypes(TypeDeclarationSyntax tds, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(tds) is not INamedTypeSymbol typeSymbol)
        {
            return new Dictionary<string, SerializableTypeMetadata>().Values;
        }

        IEnumerable<INamedTypeSymbol> derivedTypes = FindDerivedTypes(typeSymbol, model.Compilation);
        Dictionary<string, SerializableTypeMetadata> resultDict = [];

        // Process nested types
        foreach (TypeDeclarationSyntax nestedType in tds.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (Parser.ParseSerialazableType(nestedType, model) is SerializableTypeMetadata metadata)
            {
                if (!resultDict.ContainsKey(metadata.FullyQualifiedName))
                {
                    resultDict[metadata.FullyQualifiedName] = metadata;
                }
            }
        }

        // Process derived types
        foreach (INamedTypeSymbol derivedType in derivedTypes)
        {
            SyntaxReference? syntaxRef = derivedType.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef?.GetSyntax() is TypeDeclarationSyntax derivedTypeSyntax)
            {
                SemanticModel derivedTypeModel = model;
                if (syntaxRef.SyntaxTree != model.SyntaxTree)
                {
                    derivedTypeModel = model.Compilation.GetSemanticModel(syntaxRef.SyntaxTree);
                }

                if (Parser.ParseSerialazableType(derivedTypeSyntax, derivedTypeModel) is SerializableTypeMetadata metadata)
                {
                    if (!resultDict.ContainsKey(metadata.FullyQualifiedName))
                    {
                        resultDict[metadata.FullyQualifiedName] = metadata;
                    }
                }
            }
        }

        return resultDict.Values;
    }

    private static IEnumerable<INamedTypeSymbol> FindDerivedTypes(INamedTypeSymbol baseType, Compilation compilation)
    {
        return compilation
            .GetSymbolsWithName(name => true, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .Where(type => !SymbolEqualityComparer.Default.Equals(type, baseType) && type.BaseType != null && ImplementsOrInheritsFrom(type, baseType)
        );
    }

    private static bool ImplementsOrInheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        if (baseType.TypeKind == TypeKind.Interface)
        {
            return type.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, baseType.OriginalDefinition));
        }
        else
        {
            INamedTypeSymbol? currentType = type;
            while (currentType != null)
            {
                if (SymbolEqualityComparer.Default.Equals(currentType.OriginalDefinition, baseType.OriginalDefinition))
                {
                    return true;
                }

                currentType = currentType.BaseType;
            }
        }

        return false;
    }

    private static bool CanUseReaderOverload(ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol is not INamedTypeSymbol namedTypeSymbol)
        {
            return false;
        }

        bool isCborSerializable = namedTypeSymbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is Parser.CborSerializable or Parser.CborSerializableAttribute);

        if (!isCborSerializable)
        {
            return false;
        }

        bool isUnionType = namedTypeSymbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is Parser.CborUnion or Parser.CborUnionAttribute);

        if (isUnionType)
        {
            return false;
        }

        INamedTypeSymbol? preserveRawInterface = compilation.GetTypeByMetadataName(Parser.ICborPreserveRawFullName);
        return preserveRawInterface is not null
            ? !namedTypeSymbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, preserveRawInterface))
            : !namedTypeSymbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == Parser.ICborPreserveRaw ||
            i.ToDisplayString() == Parser.ICborPreserveRawFullName ||
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == $"global::{Parser.ICborPreserveRawFullName}");
    }

    private static bool IsListType(ITypeSymbol? typeSymbol, out ITypeSymbol? itemType)
    {
        itemType = null;

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            itemType = arrayType.ElementType;
            return true;
        }

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
        if (model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
        {
            if (symbol.ContainingType != null)
            {
                string containerName = symbol.ContainingType.ToDisplayString();

                string parentTypeParams = "";
                if (symbol.ContainingType.TypeParameters.Length > 0)
                {
                    parentTypeParams = "<" + string.Join(", ", symbol.ContainingType.TypeParameters.Select(tp => tp.Name)) + ">";
                }

                return (containerName, parentTypeParams);
            }

            return (symbol.ContainingNamespace.ToDisplayString(), "");
        }

        return (typeDecl.GetNamespace() ?? string.Empty, "");
    }
    private static string GetBaseNamespace(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        return model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol
            ? symbol.ContainingNamespace.ToDisplayString()
            : typeDecl.GetNamespace() ?? string.Empty;
    }

    private static string RemoveGenericPart(string identifier)
    {
        int angleBracketIndex = identifier.IndexOf('<');
        return angleBracketIndex > 0 ? identifier.Substring(0, angleBracketIndex).Trim() : identifier.Trim();
    }
}

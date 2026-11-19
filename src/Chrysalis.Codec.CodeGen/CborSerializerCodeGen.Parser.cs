using Chrysalis.Codec.CodeGen.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Chrysalis.Codec.CodeGen;


public sealed partial class CborSerializerCodeGen
{
    private static class Parser
    {
        public const string CborSerializable = "Chrysalis.Codec.Serialization.Attributes.CborSerializableAttribute";
        public const string CborMap = "CborMap";
        public const string CborNullable = "CborNullable";
        public const string CborList = "CborList";
        public const string CborUnion = "CborUnion";
        public const string CborConstr = "CborConstr";
        public const string CborTag = "CborTag";
        public const string CborOrder = "CborOrder";
        public const string CborLedgerText = "Chrysalis.Codec.Serialization.Attributes.CborLedgerTextAttribute";
        public const string CborPropertyAttribute = "CborPropertyAttribute";
        public const string CborProperty = "CborProperty";
        public const string CborSize = "CborSize";
        public const string CborIndex = "CborIndex";
        public const string CborIndefinite = "CborIndefinite";
        public const string CborDefinite = "CborDefinite";
        public const string CborUnionHint = "CborUnionHint";
        public const string CborUnionHintAttribute = "CborUnionHintAttribute";
        public const string PlutusData = "Chrysalis.Codec.Serialization.Attributes.PlutusDataAttribute";

        // Interfaces
        public const string ICborValidatorFullName = "Chrysalis.Codec.Serialization.ICborValidator`1";

        public static SerializableTypeMetadata ParseSerializableType(TypeDeclarationSyntax tds, SemanticModel model,
            TypeDeclarationSyntax[] parts, AttributeSyntax? plutusDataAttribute)
        {
            List<AttributeSyntax> attributes = [.. parts.SelectMany(part => part.AttributeLists).SelectMany(list => list.Attributes)];
            string baseNamespace = GetBaseNamespace(tds, model);
            (string path, string parentTypeParams) = GetFullNamespaceWithParentsAndTypeParams(tds, model);
            string? keyword = tds.Keyword.ToFullString().Trim();
            // A partial record struct need not repeat readonly on every declaration.
            if (tds.IsKind(SyntaxKind.RecordStructDeclaration))
            {
                keyword = "readonly record struct";
            }
            string? @namespace = baseNamespace.Length == 0 ? null : baseNamespace;

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
            // [PlutusData(N)] implies [CborConstr(N)]
            cborConstrAttribute ??= plutusDataAttribute;
            AttributeSyntax? cborTagAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborTag);
            AttributeSyntax? cborIndefiniteAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborIndefinite);
            AttributeSyntax? cborDefiniteAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborDefinite);
            int? definiteSize = cborDefiniteAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;

            int? constrIndex = cborConstrAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;
            AttributeSyntax? cborIndexAttribute = attributes.FirstOrDefault(a => a.Name.ToString() == CborIndex);
            int? cborIndexValue = cborIndexAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;
            int? cborTag = cborTagAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.GetFirstToken().Value as int?;

            // V2: All types preserve raw by default
            bool shouldPreserveRaw = true;

            SerializationType serializationType = GetSerializationType(cborMapAttribute, cborListAttribute, cborUnionAttribute, cborConstrAttribute);
            string? validatorFullyQualifiedName = FindValidatorForType(tds, model, model.Compilation);

            IEnumerable<SerializablePropertyMetadata> properties = GetPropertyMetadata(tds, model, parts);

            return new SerializableTypeMetadata(
                baseIdentifier,
                identifier,
                @namespace,
                typeParams,
                fullyQualifiedName,
                keyword,
                cborTag,
                cborIndexValue ?? constrIndex,
                cborIndefiniteAttribute != null,
                cborDefiniteAttribute != null,
                definiteSize,
                serializationType,
                validatorFullyQualifiedName,
                shouldPreserveRaw
            )
            {
                Properties = new(properties)
            };
        }
    }

    private static SerializationType GetSerializationType(AttributeSyntax? cborMapAttribute, AttributeSyntax? cborListAttribute, AttributeSyntax? cborUnionAttribute, AttributeSyntax? cborConstrAttribute) => cborMapAttribute != null
            ? SerializationType.Map
            : cborListAttribute != null
                ? SerializationType.List
                : cborUnionAttribute != null
                    ? SerializationType.Union
                    : cborConstrAttribute != null ? SerializationType.Constr : SerializationType.Container;

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

    private static IEnumerable<SerializablePropertyMetadata> GetPropertyMetadata(TypeDeclarationSyntax tds, SemanticModel model,
        TypeDeclarationSyntax[] parts)
    {
        SemanticModel ModelFor(TypeDeclarationSyntax part) => part.SyntaxTree == model.SyntaxTree ? model : model.Compilation.GetSemanticModel(part.SyntaxTree);

        if (tds is ClassDeclarationSyntax)
        {
            return parts.OfType<ClassDeclarationSyntax>().SelectMany(part => GetClassPropertyMetadata(part, ModelFor(part)));
        }
        if (tds is not RecordDeclarationSyntax record)
        {
            return [];
        }

        // Positional metadata wins if a record also declares the corresponding property explicitly.
        List<SerializablePropertyMetadata> properties = [.. GetRecordParameterMetadata(record, model)];
        int positionalCount = properties.Count;
        int contributingParts = positionalCount == 0 ? 0 : 1;
        HashSet<string> names = new(properties.Select(property => property.PropertyName), StringComparer.Ordinal);
        foreach (RecordDeclarationSyntax part in parts.OfType<RecordDeclarationSyntax>())
        {
            int previousCount = properties.Count;
            properties.AddRange(GetRecordBodyPropertyMetadata(part, ModelFor(part)).Where(property => names.Add(property.PropertyName)));
            if (properties.Count != previousCount && (positionalCount == 0 || part.SyntaxTree != record.SyntaxTree || part.Span != record.Span))
            {
                contributingParts++;
            }
        }

        // Empty and duplicate-only declarations do not change an existing signature or wire order.
        if (contributingParts < 2)
        {
            return properties;
        }

        // Remember the public argument order, then publish the one wire sequence all emitters read.
        return properties.Take(positionalCount).Concat(properties.Skip(positionalCount).OrderBy(property => property.Order ?? int.MaxValue))
            .Select((property, index) => property with { CreateParameterOrder = index })
            .OrderBy(property => property.Order ?? int.MaxValue);
    }

    private static IEnumerable<SerializablePropertyMetadata> GetClassPropertyMetadata(ClassDeclarationSyntax classDecl, SemanticModel model)
    {
        List<AttributeSyntax> attributes = [.. classDecl.AttributeLists.SelectMany(a => a.Attributes)];
        IEnumerable<PropertyDeclarationSyntax> properties = classDecl.Members.OfType<PropertyDeclarationSyntax>();

        foreach (PropertyDeclarationSyntax property in properties)
        {
            string propertyName = property.Identifier.ToFullString();
            string propertyType = property.Type.ToFullString();
            string propertyTypeFullName = model.GetSymbolInfo(property.Type).Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
            string propertyTypeNamespace = model.GetSymbolInfo(property.Type).Symbol?.ContainingNamespace.ToDisplayString() ?? string.Empty;
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
                false
            );
        }
    }

    private static IEnumerable<SerializablePropertyMetadata> GetRecordParameterMetadata(RecordDeclarationSyntax recordDecl, SemanticModel model)
    {
        foreach (ParameterSyntax param in recordDecl.ParameterList?.Parameters ?? [])
        {
            List<AttributeSyntax> paramAttributes = [.. param.AttributeLists.SelectMany(a => a.Attributes)];

            string propertyName = param.Identifier.Text;
            string propertyType = param.Type?.ToString() ?? throw new ArgumentNullException(nameof(recordDecl), "Parameter type cannot be null");
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

            bool isPropertyTypeUnion = typeSymbol is ITypeSymbol pts && HasCborUnionAttribute(pts);
            bool isListItemTypeUnion = isList && typeSymbol is ITypeSymbol lts && IsListType(lts, out ITypeSymbol? litSymbol) && litSymbol is not null && HasCborUnionAttribute(litSymbol);
            bool isMapKeyTypeUnion = isMap && typeSymbol is ITypeSymbol mks && IsMapType(mks, out ITypeSymbol? mkSymbol, out _) && mkSymbol is not null && HasCborUnionAttribute(mkSymbol);
            bool isMapValueTypeUnion = isMap && typeSymbol is ITypeSymbol mvs && IsMapType(mvs, out _, out ITypeSymbol? mvSymbol) && mvSymbol is not null && HasCborUnionAttribute(mvSymbol);

            CborListPropertyMetadata? cborList = GetCborListProperty(model.GetTypeInfo(param.Type).Type);
            if (cborList is not null)
            {
                propertyTypeFullName = cborList.Type + (cborList.IsNullable ? "?" : "");
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
                isPropertyTypeUnion,
                isListItemTypeUnion,
                isMapKeyTypeUnion,
                isMapValueTypeUnion,
                cborList,
                IsPairList(typeSymbol as ITypeSymbol, out _)
            );


            yield return WithUnionHints(model.GetDeclaredSymbol(param), propMetadata);
        }
    }

    private static IEnumerable<SerializablePropertyMetadata> GetRecordBodyPropertyMetadata(RecordDeclarationSyntax recordDecl, SemanticModel model)
    {
        bool isRecordStruct = recordDecl.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);
        // Only the assembly defining the codec attributes can use this internal hook.
        IAssemblySymbol assembly = model.Compilation.Assembly;
        INamedTypeSymbol? ledgerTextAttribute = assembly.GetTypeByMetadataName(Parser.CborSerializable) is not null
            ? assembly.GetTypeByMetadataName(Parser.CborLedgerText)
            : null;
        foreach (PropertyDeclarationSyntax prop in recordDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            IPropertySymbol? propertySymbol = model.GetDeclaredSymbol(prop);
            List<AttributeSyntax> propAttributes = [.. prop.AttributeLists.SelectMany(a => a.Attributes)];
            // Structs expose lazy fields as partial properties. Classes opt in body members
            // explicitly so helper properties are not added to the wire format.
            if (isRecordStruct ? (!prop.Modifiers.Any(SyntaxKind.PartialKeyword) || propertySymbol is not { IsPartialDefinition: true })
                : !propAttributes.Any(attribute => attribute.Name.ToString() is Parser.CborOrder or Parser.CborProperty or Parser.CborPropertyAttribute))
            {
                continue;
            }

            string propertyName = prop.Identifier.Text;
            string propertyType = prop.Type.ToString();
            string propertyTypeNamespace = string.Empty;
            bool isOpenGeneric = false;
            ISymbol? typeSymbol = model.GetSymbolInfo(prop.Type).Symbol;
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

            bool isNullable = propAttributes.Any(a => a.Name.ToString() == Parser.CborNullable);
            bool isIndefinite = propAttributes.Any(a => a.Name.ToString() == Parser.CborIndefinite);
            bool isDefinite = propAttributes.Any(a => a.Name.ToString() == Parser.CborDefinite);

            int? size = null;
            AttributeSyntax sizeAttr = propAttributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborSize);
            if (sizeAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax sizeArg)
            {
                Optional<object?> constValue = model.GetConstantValue(sizeArg.Expression);
                if (constValue.HasValue && constValue.Value is int sizeVal)
                {
                    size = sizeVal;
                }
            }

            int? order = null;
            AttributeSyntax orderAttr = propAttributes.FirstOrDefault(a => a.Name.ToString() == Parser.CborOrder);
            if (orderAttr?.ArgumentList?.Arguments.FirstOrDefault() is AttributeArgumentSyntax orderArg)
            {
                Optional<object?> constValue = model.GetConstantValue(orderArg.Expression);
                if (constValue.HasValue && constValue.Value is int orderVal)
                {
                    order = orderVal;
                }
            }

            GetCborPropertyKey(prop, model, out string? stringKey, out int? intKey);

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

            bool isPropertyTypeUnion = typeSymbol is ITypeSymbol pts && HasCborUnionAttribute(pts);
            bool isListItemTypeUnion = isList && typeSymbol is ITypeSymbol lts && IsListType(lts, out ITypeSymbol? litSymbol) && litSymbol is not null && HasCborUnionAttribute(litSymbol);
            bool isMapKeyTypeUnion = isMap && typeSymbol is ITypeSymbol mks && IsMapType(mks, out ITypeSymbol? mkSymbol, out _) && mkSymbol is not null && HasCborUnionAttribute(mkSymbol);
            bool isMapValueTypeUnion = isMap && typeSymbol is ITypeSymbol mvs && IsMapType(mvs, out _, out ITypeSymbol? mvSymbol) && mvSymbol is not null && HasCborUnionAttribute(mvSymbol);

            CborListPropertyMetadata? cborList = GetCborListProperty(model.GetTypeInfo(prop.Type).Type);
            if (cborList is not null)
            {
                propertyTypeFullName = cborList.Type + (cborList.IsNullable ? "?" : "");
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
                isPropertyTypeUnion,
                isListItemTypeUnion,
                isMapKeyTypeUnion,
                isMapValueTypeUnion,
                cborList,
                IsPairList(typeSymbol as ITypeSymbol, out _)
            )
            {
                IsLedgerText = ledgerTextAttribute is not null && propertySymbol?.GetAttributes().Any(
                    attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, ledgerTextAttribute)) == true
            };

            yield return WithUnionHints(propertySymbol, propMetadata);
        }
    }

    private static bool HasCborUnionAttribute(ITypeSymbol typeSymbol)
    {
        ITypeSymbol? current = typeSymbol;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            if (current.GetAttributes().Any(a => a.AttributeClass?.Name is Parser.CborUnion or "CborUnionAttribute"))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static CborListPropertyMetadata? GetCborListProperty(ITypeSymbol? type)
    {
        bool nullable = false;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapper)
        {
            type = wrapper.TypeArguments[0];
            nullable = true;
        }
        if (type is not INamedTypeSymbol list || !IsCborList(list))
        {
            return null;
        }

        ITypeSymbol item = list.TypeArguments[0];
        SymbolDisplayFormat format = SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        bool direct = item is INamedTypeSymbol named && item.NullableAnnotation != NullableAnnotation.Annotated && HasDirectListItemReader(named);
        return new(list.ToDisplayString(format), item.ToDisplayString(format), nullable, direct);
    }

    private static bool HasDirectListItemReader(INamedTypeSymbol item) =>
        IsCborList(item) || item.GetMembers("Read").OfType<IMethodSymbol>().Any(method => IsConsumedListItemReader(method, item)) ||
        // In this compilation Roslyn has not seen the methods this generator will emit yet.
        (item.DeclaringSyntaxReferences.Length > 0 && item.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() is Parser.CborSerializable or Parser.PlutusData));

    private static bool IsConsumedListItemReader(IMethodSymbol method, INamedTypeSymbol item)
    {
        if (!method.IsStatic || method.IsAbstract || method.IsVirtual || method.DeclaredAccessibility != Accessibility.Public ||
            method.Arity != 0 || method.Parameters.Length != 2 || !SymbolEqualityComparer.Default.Equals(method.ReturnType, item))
        {
            return false;
        }
        IParameterSymbol input = method.Parameters[0];
        IParameterSymbol count = method.Parameters[1];
        return input.RefKind == RefKind.None && input.Type is INamedTypeSymbol { Name: "ReadOnlyMemory", Arity: 1 } memory &&
            memory.ContainingNamespace.ToDisplayString() == "System" && memory.TypeArguments[0].SpecialType == SpecialType.System_Byte &&
            count.RefKind == RefKind.Out && count.Type.SpecialType == SpecialType.System_Int32;
    }

    private static bool IsCborList(INamedTypeSymbol type) => type is { Name: "CborList", Arity: 1 } &&
        type.ContainingNamespace.ToDisplayString() == "Chrysalis.Codec.Types";

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

    private static SerializablePropertyMetadata WithUnionHints(ISymbol? symbol, SerializablePropertyMetadata property)
    {
        if (symbol is null)
        {
            return property;
        }

        Dictionary<int, string> hints = [];
        string? discriminant = null;
        foreach (AttributeData attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not (Parser.CborUnionHint or Parser.CborUnionHintAttribute)
                || attr.ConstructorArguments.Length < 3)
            {
                continue;
            }

            if (attr.ConstructorArguments[0].Value is string name
                && attr.ConstructorArguments[1].Value is int value
                && attr.ConstructorArguments[2].Value is INamedTypeSymbol concreteType)
            {
                hints[value] = concreteType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                discriminant ??= name;
            }
        }

        return hints.Count == 0 ? property : property with
        {
            UnionHints = new(hints),
            UnionHintDiscriminantProperty = discriminant
        };
    }

    private static bool IsListType(ITypeSymbol? typeSymbol, out ITypeSymbol? itemType)
    {
        itemType = null;

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            itemType = arrayType.ElementType;
            return true;
        }

        if (typeSymbol is INamedTypeSymbol namedType && namedType.SpecialType != SpecialType.System_String && !IsCborList(namedType))
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

    private static bool IsPairList(ITypeSymbol? symbol, out INamedTypeSymbol? pair)
    {
        pair = null;
        if (symbol is not INamedTypeSymbol list || list.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.List<T>"
            || list.TypeArguments[0] is not INamedTypeSymbol item
            || item.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.KeyValuePair<TKey, TValue>")
        {
            return false;
        }

        pair = item;
        return true;
    }

    private static bool IsMapType(ITypeSymbol typeSymbol, out ITypeSymbol? keyType, out ITypeSymbol? valueType)
    {
        keyType = null;
        valueType = null;

        if (IsPairList(typeSymbol, out INamedTypeSymbol? pair))
        {
            keyType = pair!.TypeArguments[0];
            valueType = pair.TypeArguments[1];
            return true;
        }

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

            return (DisplayNamespace(symbol.ContainingNamespace), "");
        }

        return (typeDecl.GetNamespace() ?? string.Empty, "");
    }

    private static string GetBaseNamespace(TypeDeclarationSyntax typeDecl, SemanticModel model) => model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol
            ? DisplayNamespace(symbol.ContainingNamespace)
            : typeDecl.GetNamespace() ?? string.Empty;

    private static string DisplayNamespace(INamespaceSymbol symbol) => symbol.IsGlobalNamespace ? "" : symbol.ToDisplayString();

    private static string RemoveGenericPart(string identifier)
    {
        int angleBracketIndex = identifier.IndexOf('<');
        return angleBracketIndex > 0 ? identifier.Substring(0, angleBracketIndex).Trim() : identifier.Trim();
    }
}

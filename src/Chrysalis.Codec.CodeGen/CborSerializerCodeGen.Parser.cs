using System.Collections.Immutable;
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
        public const string CborUnionFullName = "Chrysalis.Codec.Serialization.Attributes.CborUnionAttribute";
        public const string CborMapFullName = "Chrysalis.Codec.Serialization.Attributes.CborMapAttribute";
        public const string CborListFullName = "Chrysalis.Codec.Serialization.Attributes.CborListAttribute";
        public const string CborConstrFullName = "Chrysalis.Codec.Serialization.Attributes.CborConstrAttribute";
        public const string CborTagFullName = "Chrysalis.Codec.Serialization.Attributes.CborTagAttribute";
        public const string CborIndexFullName = "Chrysalis.Codec.Serialization.Attributes.CborIndexAttribute";
        public const string CborOrderFullName = "Chrysalis.Codec.Serialization.Attributes.CborOrderAttribute";
        public const string CborPropertyFullName = "Chrysalis.Codec.Serialization.Attributes.CborPropertyAttribute";
        public const string CborNullableFullName = "Chrysalis.Codec.Serialization.Attributes.CborNullableAttribute";
        public const string CborSizeFullName = "Chrysalis.Codec.Serialization.Attributes.CborSizeAttribute";
        public const string CborIndefiniteFullName = "Chrysalis.Codec.Serialization.Attributes.CborIndefiniteAttribute";
        public const string CborDefiniteFullName = "Chrysalis.Codec.Serialization.Attributes.CborDefiniteAttribute";
        public const string UnionAttributeFullName = "System.Runtime.CompilerServices.UnionAttribute";
        public const string ICborTypeFullName = "Chrysalis.Codec.Serialization.ICborType";
        public const string CborLedgerText = "Chrysalis.Codec.Serialization.Attributes.CborLedgerTextAttribute";
        public const string CborUnionHint = "CborUnionHint";
        public const string CborUnionHintAttribute = "CborUnionHintAttribute";
        public const string PlutusData = "Chrysalis.Codec.Serialization.Attributes.PlutusDataAttribute";
        public const string ICborValidatorFullName = "Chrysalis.Codec.Serialization.ICborValidator`1";

        private static readonly SymbolDisplayFormat NullableDisplayFormat =
            SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        public static SerializableTypeMetadata ParseSerializableType(TypeDeclarationSyntax tds, SemanticModel model,
            TypeDeclarationSyntax[] parts)
        {
            INamedTypeSymbol? typeSymbol = model.GetDeclaredSymbol(tds);
            ImmutableArray<AttributeData> attrs = typeSymbol?.GetAttributes() ?? [];
            bool hasSemanticUnion = FindAttr(attrs, CborUnionFullName) is not null;
            string baseNamespace = GetBaseNamespace(tds, model);
            (string path, string parentTypeParams) = GetFullNamespaceWithParentsAndTypeParams(tds, model);
            string keyword = tds.Keyword.ToFullString().Trim();
            // A partial record struct need not repeat readonly on every declaration.
            if (tds.IsKind(SyntaxKind.RecordStructDeclaration))
            {
                keyword = "readonly record struct";
            }
            else if (tds.IsKind(SyntaxKind.StructDeclaration) && typeSymbol is { IsReadOnly: true } && hasSemanticUnion)
            {
                keyword = "readonly struct";
            }

            string? @namespace = baseNamespace.Length == 0 ? null : baseNamespace;
            string? typeParams = tds.TypeParameterList?.ToString() ?? parentTypeParams;
            string baseIdentifier = RemoveGenericPart(tds.Identifier.ToFullString());
            string identifier = $"{baseIdentifier}{typeParams}";
            string fullyQualifiedName = string.IsNullOrEmpty(path) ? baseIdentifier : $"{path}.{baseIdentifier}";
            if (string.IsNullOrEmpty(parentTypeParams))
            {
                fullyQualifiedName = $"{fullyQualifiedName}{typeParams}";
            }

            (SerializationType serializationType, int? cborTag, int? cborIndex, bool isIndefinite, bool isDefinite, int? definiteSize) = ParseWireAttributes(attrs);
            string? validatorFullyQualifiedName = FindValidatorForType(typeSymbol, model.Compilation);

            IEnumerable<SerializablePropertyMetadata> properties = GetPropertyMetadata(tds, model, parts);
            if (serializationType == SerializationType.Constr)
            {
                properties = properties.Select(WithPlutusBoundedBytesSize);
            }

            return new SerializableTypeMetadata(
                baseIdentifier,
                identifier,
                @namespace,
                typeParams,
                fullyQualifiedName,
                keyword,
                cborTag,
                cborIndex,
                isIndefinite,
                isDefinite,
                definiteSize,
                serializationType,
                validatorFullyQualifiedName,
                true
            )
            {
                IsValueType = typeSymbol?.IsValueType ?? (tds.IsKind(SyntaxKind.StructDeclaration) || tds.IsKind(SyntaxKind.RecordStructDeclaration)),
                IsAbstract = typeSymbol?.IsAbstract ?? false,
                Properties = new(properties)
            };
        }

        public static SerializableTypeMetadata ParseSymbolWireShape(INamedTypeSymbol boundSymbol, Compilation compilation)
        {
            ImmutableArray<AttributeData> attrs = boundSymbol.GetAttributes();
            bool hasSerializable = FindAttr(attrs, CborSerializable) is not null || FindAttr(attrs, PlutusData) is not null;

            string? @namespace = boundSymbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null;
            string typeParams = boundSymbol.TypeArguments.Length > 0
                ? "<" + string.Join(", ", boundSymbol.TypeArguments.Select(ta => ta.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ">"
                : "";
            string identifier = boundSymbol.Name + typeParams;
            string fqName = boundSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string keyword = boundSymbol.IsValueType
                ? (boundSymbol.IsRecord ? "readonly record struct" : boundSymbol.IsReadOnly ? "readonly struct" : "struct")
                : (boundSymbol.IsRecord ? "record" : "class");

            if (!hasSerializable)
            {
                return new SerializableTypeMetadata(
                    boundSymbol.Name, identifier, @namespace, string.IsNullOrEmpty(typeParams) ? null : typeParams, fqName, keyword,
                    null, null, false, false, null, SerializationType.Container, null, true)
                {
                    IsValueType = boundSymbol.IsValueType
                };
            }

            (SerializationType serializationType, int? cborTag, int? cborIndex, bool isIndefinite, bool isDefinite, int? definiteSize) = ParseWireAttributes(attrs);
            bool hasAmbiguousWireOrder = false;

            IEnumerable<SerializablePropertyMetadata> properties;
            INamedTypeSymbol orig = boundSymbol.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(orig.ContainingAssembly, compilation.Assembly) && orig.DeclaringSyntaxReferences.Length > 0)
            {
                TypeDeclarationSyntax[] parts = [.. orig.DeclaringSyntaxReferences
                    .OrderBy(r => r.SyntaxTree.FilePath, StringComparer.Ordinal)
                    .ThenBy(r => r.Span.Start)
                    .Select(r => (TypeDeclarationSyntax)r.GetSyntax())];
                TypeDeclarationSyntax payload = parts.OfType<RecordDeclarationSyntax>().FirstOrDefault(p => p.ParameterList is not null) ?? parts[0];
                SemanticModel model = compilation.GetSemanticModel(payload.SyntaxTree);
                IEnumerable<SerializablePropertyMetadata> origProps = GetPropertyMetadata(payload, model, parts);
                properties = SymbolEqualityComparer.Default.Equals(boundSymbol, orig)
                    ? origProps
                    : origProps.Select(origProp =>
                    {
                        IPropertySymbol? boundProp = boundSymbol.GetMembers(origProp.PropertyName).OfType<IPropertySymbol>().FirstOrDefault();
                        ITypeSymbol? boundType = boundProp?.Type;
                        string? propType = boundType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? origProp.PropertyType;
                        SerializablePropertyMetadata rebuilt = BuildPropertyMetadata(
                            origProp.PropertyName, propType, boundType, origProp.IsNullable, origProp.IsIndefinite,
                            origProp.IsDefinite, origProp.Size, origProp.Order, origProp.PropertyKeyString,
                            origProp.PropertyKeyInt, origProp.IsLedgerText, boundProp);
                        return rebuilt with
                        {
                            CreateParameterOrder = origProp.CreateParameterOrder,
                            UnionHints = origProp.UnionHints,
                            UnionHintDiscriminantProperty = origProp.UnionHintDiscriminantProperty
                        };
                    });
            }
            else
            {
                properties = ExtractExternalProperties(boundSymbol, serializationType, cborIndex, out hasAmbiguousWireOrder);
            }

            if (serializationType == SerializationType.Constr)
            {
                properties = properties.Select(WithPlutusBoundedBytesSize);
            }

            return new SerializableTypeMetadata(
                boundSymbol.Name, identifier, @namespace, string.IsNullOrEmpty(typeParams) ? null : typeParams, fqName, keyword,
                cborTag, cborIndex, isIndefinite, isDefinite, definiteSize, serializationType, null, true)
            {
                IsValueType = boundSymbol.IsValueType,
                HasAmbiguousExternalWireOrder = hasAmbiguousWireOrder,
                Properties = new(properties)
            };
        }

        private static List<SerializablePropertyMetadata> ExtractExternalProperties(
            INamedTypeSymbol boundSymbol, SerializationType serializationType, int? cborIndex, out bool hasAmbiguousWireOrder)
        {
            hasAmbiguousWireOrder = false;
            List<IPropertySymbol> instanceProps = [.. boundSymbol.GetMembers().OfType<IPropertySymbol>()
                .Where(p => !p.IsStatic && !p.IsIndexer && p.Name is not ("EqualityContract" or "Raw" or "ConstrIndex" or "IsIndefinite"))];
            Dictionary<string, IPropertySymbol> propByName = [];
            foreach (IPropertySymbol prop in instanceProps)
            {
                if (!propByName.ContainsKey(prop.Name)) { propByName[prop.Name] = prop; }
            }

            List<(IPropertySymbol Prop, ImmutableArray<AttributeData> Attrs)> selected = [];
            IMethodSymbol? primaryCtor = boundSymbol.IsRecord
                ? boundSymbol.InstanceConstructors
                    .Where(c => c.Parameters.Length > 0
                        && !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, boundSymbol))
                        && c.Parameters.All(p => propByName.ContainsKey(p.Name)))
                    .OrderByDescending(c => c.Parameters.Length)
                    .FirstOrDefault()
                : null;

            if (primaryCtor is not null)
            {
                HashSet<string> used = [with(StringComparer.Ordinal)];
                foreach (IParameterSymbol param in primaryCtor.Parameters)
                {
                    IPropertySymbol prop = propByName[param.Name];
                    _ = used.Add(prop.Name);
                    selected.Add((prop, [.. param.GetAttributes(), .. prop.GetAttributes()]));
                }
                foreach (IPropertySymbol prop in instanceProps)
                {
                    if (used.Contains(prop.Name)) { continue; }
                    ImmutableArray<AttributeData> pAttrs = prop.GetAttributes();
                    if (FindAttr(pAttrs, CborOrderFullName) is not null || FindAttr(pAttrs, CborPropertyFullName) is not null)
                    {
                        selected.Add((prop, pAttrs));
                    }
                }
            }
            else
            {
                bool skipDyn = serializationType == SerializationType.Constr && (cborIndex is null or < 0);
                HashSet<string> explicitNames = [with(StringComparer.Ordinal)];
                foreach (IPropertySymbol p in instanceProps)
                {
                    ImmutableArray<AttributeData> a = p.GetAttributes();
                    if (FindAttr(a, CborOrderFullName) is not null || FindAttr(a, CborPropertyFullName) is not null) { _ = explicitNames.Add(p.Name); }
                }

                List<HashSet<string>> sets = [];
                foreach (IMethodSymbol m in boundSymbol.GetMembers("Create").OfType<IMethodSymbol>().Where(m =>
                    m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Arity == 0 && SymbolEqualityComparer.Default.Equals(m.ReturnType, boundSymbol)
                    && (!skipDyn || (m.Parameters.Length > 0 && m.Parameters[0].Type.SpecialType == SpecialType.System_Int32))))
                {
                    HashSet<string> matched = [with(StringComparer.Ordinal)];
                    bool ok = (skipDyn ? m.Parameters.Skip(1) : m.Parameters).All(param =>
                    {
                        if (param.RefKind != RefKind.None) { return false; }
                        IPropertySymbol[] hits = [.. instanceProps.Where(p => Emitter.ToCreateParameterName(p.Name) == param.Name && SymbolEqualityComparer.Default.Equals(param.Type, p.Type))];
                        return hits.Length == 1 && matched.Add(hits[0].Name);
                    });
                    if (ok && explicitNames.All(matched.Contains) && !sets.Any(s => s.SetEquals(matched))) { sets.Add(matched); }
                }

                HashSet<string>? picked = sets.Count == 1 ? sets[0]
                    : sets.Count > 1 && explicitNames.Count > 0 ? sets.FirstOrDefault(s => s.Count == explicitNames.Count) : null;
                if (sets.Count > 1 && picked is null) { hasAmbiguousWireOrder = true; picked = explicitNames; }

                foreach (IPropertySymbol prop in instanceProps)
                {
                    ImmutableArray<AttributeData> pAttrs = prop.GetAttributes();
                    bool keep = picked is not null ? picked.Contains(prop.Name)
                        : explicitNames.Contains(prop.Name) || (explicitNames.Count == 0 && serializationType == SerializationType.Container && prop.SetMethod is not null);
                    if (keep) { selected.Add((prop, pAttrs)); }
                }
            }

            List<SerializablePropertyMetadata> props = [with(selected.Count)];
            foreach ((IPropertySymbol prop, ImmutableArray<AttributeData> attrs) in selected)
            {
                string propType = prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                props.Add(BuildPropertyMetadata(prop.Name, propType, prop.Type, attrs, FindAttr(attrs, CborLedgerText) is not null, prop));
            }

            if (serializationType == SerializationType.List && props.Count >= 2)
            {
                for (int i = 0; i < props.Count; i++)
                {
                    if (!props[i].Order.HasValue || (i > 0 && props[i].Order!.Value <= props[i - 1].Order!.Value))
                    {
                        hasAmbiguousWireOrder = true;
                        break;
                    }
                }
            }

            return props;
        }

        private static AttributeData? FindAttr(ImmutableArray<AttributeData> attrs, string fullName) =>
            attrs.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fullName);

        private static int? GetIntArg(AttributeData? attr) =>
            attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Kind != TypedConstantKind.Array
                && attr.ConstructorArguments[0].Value is int val ? val : null;

        private static (SerializationType SerializationType, int? CborTag, int? CborIndex, bool IsIndefinite, bool IsDefinite, int? DefiniteSize)
            ParseWireAttributes(ImmutableArray<AttributeData> attrs)
        {
            AttributeData? constr = FindAttr(attrs, CborConstrFullName) ?? FindAttr(attrs, PlutusData);
            AttributeData? definite = FindAttr(attrs, CborDefiniteFullName);
            SerializationType serializationType = FindAttr(attrs, CborMapFullName) is not null ? SerializationType.Map
                : FindAttr(attrs, CborListFullName) is not null ? SerializationType.List
                : FindAttr(attrs, CborUnionFullName) is not null ? SerializationType.Union
                : constr is not null ? SerializationType.Constr
                : SerializationType.Container;
            return (serializationType, GetIntArg(FindAttr(attrs, CborTagFullName)), GetIntArg(FindAttr(attrs, CborIndexFullName)) ?? GetIntArg(constr),
                FindAttr(attrs, CborIndefiniteFullName) is not null, definite is not null, GetIntArg(definite));
        }

        private const int PlutusBoundedBytesSize = 64;

        private static int CoveredByteStrings(SerializablePropertyMetadata property) =>
            new[] { property.PropertyType, property.ListItemTypeFullName, property.MapKeyTypeFullName, property.MapValueTypeFullName }.Count(IsByteString);

        private static SerializablePropertyMetadata WithPlutusBoundedBytesSize(SerializablePropertyMetadata property) =>
            property.Size is null && CoveredByteStrings(property) > 0 ? property with { Size = PlutusBoundedBytesSize } : property;

        private static bool IsByteString(string? type) => type is not null && (type.Replace("?", "") == "byte[]" || Emitter.IsReadOnlyMemoryByteType(type));

        public static bool HidesUnchunkedBytes(SerializablePropertyMetadata property) =>
            ByteStringCount(property.PropertyTypeFullName) > CoveredByteStrings(property);

        private static int ByteStringCount(string type) => ((type.Length - type.Replace("byte[]", "").Length) / "byte[]".Length)
            + ((type.Length - type.Replace("ReadOnlyMemory<byte>", "").Length) / "ReadOnlyMemory<byte>".Length);

        private static string? FindValidatorForType(INamedTypeSymbol? typeSymbol, Compilation compilation)
        {
            if (typeSymbol is null) { return null; }
            INamedTypeSymbol? openValidatorInterface = compilation.GetTypeByMetadataName(ICborValidatorFullName);
            if (openValidatorInterface == null) { return null; }

            INamedTypeSymbol closedValidatorInterface = openValidatorInterface.Construct(typeSymbol);
            INamedTypeSymbol? validator = compilation
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

            if (contributingParts < 2)
            {
                return properties;
            }

            return properties.Take(positionalCount).Concat(properties.Skip(positionalCount).OrderBy(property => property.Order ?? int.MaxValue))
                .Select((property, index) => property with { CreateParameterOrder = index })
                .OrderBy(property => property.Order ?? int.MaxValue);
        }

        private static IEnumerable<SerializablePropertyMetadata> GetClassPropertyMetadata(ClassDeclarationSyntax classDecl, SemanticModel model)
        {
            ImmutableArray<AttributeData> classAttrs = model.GetDeclaredSymbol(classDecl)?.GetAttributes() ?? [];
            foreach (PropertyDeclarationSyntax property in classDecl.Members.OfType<PropertyDeclarationSyntax>())
            {
                string propertyName = property.Identifier.ToFullString();
                string propertyType = property.Type.ToFullString();
                ITypeSymbol? typeSymbol = model.GetTypeInfo(property.Type).Type ?? model.GetSymbolInfo(property.Type).Symbol as ITypeSymbol;
                string propertyTypeFullName = typeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
                string propertyTypeNamespace = typeSymbol?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                ImmutableArray<AttributeData> attrs = [.. model.GetDeclaredSymbol(property)?.GetAttributes() ?? [], .. classAttrs];
                bool isNullable = FindAttr(attrs, CborNullableFullName) is not null;
                bool isIndefinite = FindAttr(attrs, CborIndefiniteFullName) is not null;
                bool isDefinite = FindAttr(attrs, CborDefiniteFullName) is not null;
                int? size = GetIntArg(FindAttr(attrs, CborSizeFullName));
                int? order = GetIntArg(FindAttr(attrs, CborOrderFullName));
                GetCborPropertyKey(attrs, out string? stringKey, out int? intKey);

                yield return new SerializablePropertyMetadata(
                    propertyName, propertyType, propertyTypeFullName, propertyTypeNamespace,
                    false, false, null, null, null, false, false, false, null, null, null, null, null, null,
                    isNullable, size, isIndefinite, isDefinite, order, stringKey, intKey, false);
            }
        }

        private static IEnumerable<SerializablePropertyMetadata> GetRecordParameterMetadata(RecordDeclarationSyntax recordDecl, SemanticModel model)
        {
            foreach (ParameterSyntax param in recordDecl.ParameterList?.Parameters ?? [])
            {
                IParameterSymbol? paramSymbol = model.GetDeclaredSymbol(param);
                IPropertySymbol? propertySymbol = paramSymbol?.ContainingType?.GetMembers(paramSymbol.Name).OfType<IPropertySymbol>().FirstOrDefault();
                ImmutableArray<AttributeData> attrs = [.. paramSymbol?.GetAttributes() ?? [], .. propertySymbol?.GetAttributes() ?? []];
                ITypeSymbol? typeSymbol = param.Type is not null
                    ? (model.GetTypeInfo(param.Type).Type ?? model.GetSymbolInfo(param.Type).Symbol as ITypeSymbol)
                    : paramSymbol?.Type;
                string propertyType = param.Type?.ToString() ?? throw new ArgumentNullException(nameof(recordDecl), "Parameter type cannot be null");
                yield return BuildPropertyMetadata(param.Identifier.Text, propertyType, typeSymbol, attrs, false, paramSymbol);
            }
        }

        private static IEnumerable<SerializablePropertyMetadata> GetRecordBodyPropertyMetadata(RecordDeclarationSyntax recordDecl, SemanticModel model)
        {
            bool isRecordStruct = recordDecl.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);
            IAssemblySymbol assembly = model.Compilation.Assembly;
            INamedTypeSymbol? ledgerTextAttribute = assembly.GetTypeByMetadataName(CborSerializable) is not null
                ? assembly.GetTypeByMetadataName(CborLedgerText)
                : null;
            foreach (PropertyDeclarationSyntax prop in recordDecl.Members.OfType<PropertyDeclarationSyntax>())
            {
                IPropertySymbol? propertySymbol = model.GetDeclaredSymbol(prop);
                ImmutableArray<AttributeData> attrs = propertySymbol?.GetAttributes() ?? [];
                if (isRecordStruct ? (!prop.Modifiers.Any(SyntaxKind.PartialKeyword) || propertySymbol is not { IsPartialDefinition: true })
                    : FindAttr(attrs, CborOrderFullName) is null && FindAttr(attrs, CborPropertyFullName) is null)
                {
                    continue;
                }

                ITypeSymbol? typeSymbol = model.GetTypeInfo(prop.Type).Type ?? model.GetSymbolInfo(prop.Type).Symbol as ITypeSymbol ?? propertySymbol?.Type;
                bool isLedgerText = ledgerTextAttribute is not null && attrs.Any(
                    attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, ledgerTextAttribute));

                yield return BuildPropertyMetadata(prop.Identifier.Text, prop.Type.ToString(), typeSymbol, attrs, isLedgerText, propertySymbol);
            }
        }

        private static SerializablePropertyMetadata BuildPropertyMetadata(
            string propertyName, string propertyType, ITypeSymbol? typeSymbol,
            ImmutableArray<AttributeData> attrs, bool isLedgerText, ISymbol? hintSymbol)
        {
            GetCborPropertyKey(attrs, out string? stringKey, out int? intKey);
            return BuildPropertyMetadata(propertyName, propertyType, typeSymbol,
                FindAttr(attrs, CborNullableFullName) is not null,
                FindAttr(attrs, CborIndefiniteFullName) is not null,
                FindAttr(attrs, CborDefiniteFullName) is not null,
                GetIntArg(FindAttr(attrs, CborSizeFullName)), GetIntArg(FindAttr(attrs, CborOrderFullName)),
                stringKey, intKey, isLedgerText, hintSymbol);
        }

        private static SerializablePropertyMetadata BuildPropertyMetadata(
            string propertyName,
            string propertyType,
            ITypeSymbol? typeSymbol,
            bool isNullable,
            bool isIndefinite,
            bool isDefinite,
            int? size,
            int? order,
            string? stringKey,
            int? intKey,
            bool isLedgerText,
            ISymbol? hintSymbol)
        {
            bool isOpenGeneric = false;
            string propertyTypeFullName = "";
            string propertyTypeNamespace = string.Empty;
            string? typeofTypeFullName = null;
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
            bool isPropertyTypeUnion = false;
            bool isListItemTypeUnion = false;
            bool isMapKeyTypeUnion = false;
            bool isMapValueTypeUnion = false;
            CborListPropertyMetadata? cborList = null;
            CborMapPropertyMetadata? cborMap = null;
            bool isPairList = false;

            if (typeSymbol is not null)
            {
                isOpenGeneric = typeSymbol.TypeKind is TypeKind.TypeParameter;
                propertyTypeFullName = typeSymbol.ToDisplayString(NullableDisplayFormat);
                propertyTypeNamespace = typeSymbol switch
                {
                    INamedTypeSymbol { ContainingNamespace: { IsGlobalNamespace: false } ns } => ns.ToDisplayString(),
                    IArrayTypeSymbol { ElementType.ContainingNamespace: { IsGlobalNamespace: false } elemNs } => elemNs.ToDisplayString(),
                    _ => string.Empty
                };

                ITypeSymbol typeofTarget = typeSymbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableValue
                    ? nullableValue.TypeArguments[0]
                    : typeSymbol;
                typeofTypeFullName = StripOuterNullability(typeofTarget.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

                if (IsMapType(typeSymbol, out ITypeSymbol? keyTypeSymbol, out ITypeSymbol? valueTypeSymbol))
                {
                    isMap = true;
                    if (keyTypeSymbol is not null)
                    {
                        mapKeyType = keyTypeSymbol.Name;
                        mapKeyTypeFullName = keyTypeSymbol.ToDisplayString(NullableDisplayFormat);
                        mapKeyTypeNamespace = keyTypeSymbol.ContainingNamespace?.IsGlobalNamespace == false ? keyTypeSymbol.ContainingNamespace.ToDisplayString() : null;
                        isMapKeyTypeOpenGeneric = HasTypeParameter(keyTypeSymbol);
                        isMapKeyTypeUnion = HasCborUnionAttribute(keyTypeSymbol);
                    }
                    if (valueTypeSymbol is not null)
                    {
                        mapValueType = valueTypeSymbol.Name;
                        mapValueTypeFullName = valueTypeSymbol.ToDisplayString(NullableDisplayFormat);
                        mapValueTypeNamespace = valueTypeSymbol.ContainingNamespace?.IsGlobalNamespace == false ? valueTypeSymbol.ContainingNamespace.ToDisplayString() : null;
                        isMapValueTypeOpenGeneric = HasTypeParameter(valueTypeSymbol);
                        isMapValueTypeUnion = HasCborUnionAttribute(valueTypeSymbol);
                    }
                }
                else if (IsListType(typeSymbol, out ITypeSymbol? itemTypeSymbol))
                {
                    isList = true;
                    if (itemTypeSymbol is not null)
                    {
                        listItemType = itemTypeSymbol.Name;
                        listItemTypeFullName = itemTypeSymbol.ToDisplayString(NullableDisplayFormat);
                        listItemTypeNamespace = itemTypeSymbol.ContainingNamespace?.IsGlobalNamespace == false ? itemTypeSymbol.ContainingNamespace.ToDisplayString() : null;
                        isListItemTypeOpenGeneric = HasTypeParameter(itemTypeSymbol);
                        isListItemTypeUnion = HasCborUnionAttribute(itemTypeSymbol);
                    }
                }

                isPropertyTypeUnion = HasCborUnionAttribute(typeSymbol);
                cborList = GetCborListProperty(typeSymbol);
                cborMap = cborList is null ? GetCborMapProperty(typeSymbol) : null;
                if (cborList is not null) { propertyTypeFullName = cborList.Type + (cborList.IsNullable ? "?" : ""); }
                else if (cborMap is not null) { propertyTypeFullName = cborMap.Type + (cborMap.IsNullable ? "?" : ""); }
                isPairList = IsPairList(typeSymbol, out _);
            }

            SerializablePropertyMetadata propMetadata = new(
                propertyName, propertyType, propertyTypeFullName, propertyTypeNamespace,
                isList, isMap, listItemType, listItemTypeFullName, listItemTypeNamespace,
                isListItemTypeOpenGeneric, isMapKeyTypeOpenGeneric, isMapValueTypeOpenGeneric,
                mapKeyType, mapValueType, mapKeyTypeFullName, mapValueTypeFullName,
                mapKeyTypeNamespace, mapValueTypeNamespace,
                isNullable, size, isIndefinite, isDefinite, order, stringKey, intKey, isOpenGeneric,
                isPropertyTypeUnion, isListItemTypeUnion, isMapKeyTypeUnion, isMapValueTypeUnion,
                cborList, isPairList, cborMap)
            {
                TypeofTypeFullName = typeofTypeFullName,
                IsLedgerText = isLedgerText
            };

            return WithUnionHints(hintSymbol, propMetadata);
        }

        private static bool HasTypeParameter(ITypeSymbol? symbol) =>
            symbol?.TypeKind == TypeKind.TypeParameter
            || (symbol is INamedTypeSymbol { IsGenericType: true } named && named.TypeArguments.Any(ta => ta.TypeKind == TypeKind.TypeParameter));

        public static bool HasCborUnionAttribute(ITypeSymbol typeSymbol)
        {
            ITypeSymbol? current = typeSymbol;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                if (FindAttr(current.GetAttributes(), CborUnionFullName) is not null)
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
            return type is INamedTypeSymbol list && IsCborList(list) ? new(list.ToDisplayString(NullableDisplayFormat), nullable) : null;
        }

        private static CborMapPropertyMetadata? GetCborMapProperty(ITypeSymbol? type)
        {
            bool nullable = false;
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapper)
            {
                type = wrapper.TypeArguments[0];
                nullable = true;
            }
            return type is INamedTypeSymbol map && IsCborMap(map) ? new(map.ToDisplayString(NullableDisplayFormat), nullable) : null;
        }

        private static bool IsCborList(INamedTypeSymbol type) => type is { Name: "CborList", Arity: 1 } &&
            type.ContainingNamespace.ToDisplayString() == "Chrysalis.Codec.Types";

        private static bool IsCborMap(INamedTypeSymbol type) => type is { Name: "CborMap", Arity: 2 } &&
            type.ContainingNamespace.ToDisplayString() == "Chrysalis.Codec.Types";

        private static void GetCborPropertyKey(ImmutableArray<AttributeData> attrs, out string? stringKey, out int? intKey)
        {
            stringKey = null;
            intKey = null;
            AttributeData? attribute = FindAttr(attrs, CborPropertyFullName);
            if (attribute?.ConstructorArguments.FirstOrDefault() is TypedConstant arg && arg.Kind == TypedConstantKind.Primitive)
            {
                if (arg.Value is int intValue) { intKey = intValue; }
                else if (arg.Value is string stringValue) { stringKey = stringValue; }
            }
        }

        private static SerializablePropertyMetadata WithUnionHints(ISymbol? symbol, SerializablePropertyMetadata property)
        {
            if (symbol is null) { return property; }
            Dictionary<int, string> hints = [];
            string? discriminant = null;
            foreach (AttributeData attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.Name is not (CborUnionHint or CborUnionHintAttribute) || attr.ConstructorArguments.Length < 3) { continue; }
                if (attr.ConstructorArguments[0].Value is string name
                    && attr.ConstructorArguments[1].Value is int value
                    && attr.ConstructorArguments[2].Value is INamedTypeSymbol concreteType)
                {
                    hints[value] = concreteType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    discriminant ??= name;
                }
            }
            return hints.Count == 0 ? property : property with { UnionHints = new(hints), UnionHintDiscriminantProperty = discriminant };
        }

        private static bool IsListType(ITypeSymbol? typeSymbol, out ITypeSymbol? itemType)
        {
            itemType = null;
            if (typeSymbol is IArrayTypeSymbol arrayType) { itemType = arrayType.ElementType; return true; }
            if (typeSymbol is INamedTypeSymbol namedType && namedType.SpecialType != SpecialType.System_String && !IsCborList(namedType) && !IsCborMap(namedType))
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
            if (typeSymbol is INamedTypeSymbol namedType && !IsCborMap(namedType))
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
                    string parentTypeParams = symbol.ContainingType.TypeParameters.Length > 0
                        ? "<" + string.Join(", ", symbol.ContainingType.TypeParameters.Select(tp => tp.Name)) + ">"
                        : "";
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
}

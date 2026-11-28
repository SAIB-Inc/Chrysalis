using Chrysalis.Codec.CodeGen.Blueprint.Analysis;
using Microsoft.CodeAnalysis;
using static Chrysalis.Codec.CodeGen.CborSerializerCodeGen;

namespace Chrysalis.Codec.CodeGen.Blueprint.Generation;

/// <summary>
/// Converts blueprint ResolvedType objects to SerializableTypeMetadata for the CBOR emitter.
/// </summary>
internal static class MetadataConverter
{
    /// <summary>
    /// Emits CBOR serializers for all resolved blueprint types.
    /// </summary>
    public static void EmitSerializers(SourceProductionContext context, string ns, string safeTitle, Dictionary<string, ResolvedType> resolvedTypes)
    {
        HashSet<string> emittedLocalTypes = [with(StringComparer.Ordinal)];
        Dictionary<string, RegistrationEntry> registrations = [with(StringComparer.Ordinal)];
        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            ResolvedType type = kv.Value;
            switch (type.Category)
            {
                case TypeCategory.SingleConstructor: EmitSingleConstructor(context, ns, type, emittedLocalTypes, registrations); break;
                case TypeCategory.Union: EmitUnion(context, ns, type, emittedLocalTypes, registrations); break;
                case TypeCategory.Tuple: EmitTuple(context, ns, type, emittedLocalTypes, registrations); break;
                case TypeCategory.PrimitiveBytes:
                case TypeCategory.PrimitiveInteger:
                case TypeCategory.PrimitiveBool:
                case TypeCategory.PrimitiveData:
                case TypeCategory.Option:
                case TypeCategory.List:
                case TypeCategory.Ref:
                default: break;
            }
        }
        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            ResolvedType type = kv.Value;
            if (type.Category is TypeCategory.Option or TypeCategory.List && TryResolveGlobalRegistrationName(SchemaResolver.GetCSharpTypeName(type), ns, emittedLocalTypes, out string globalFqn))
            {
                registrations[globalFqn] = new RegistrationEntry(globalFqn, type.Category == TypeCategory.List);
            }
        }
        if (registrations.Count > 0)
        {
            Emitter.EmitRegistrationSource(context, $"{safeTitle}.BlueprintRegistration.g.cs", "ChrysalisBlueprintRegistration", new(registrations.Values.OrderBy(static e => e.FullyQualifiedName, StringComparer.Ordinal)));
        }
    }

    private static void EmitSingleConstructor(SourceProductionContext context, string ns, ResolvedType type, HashSet<string> emittedLocalTypes, Dictionary<string, RegistrationEntry> registrations)
    {
        if (type.Constructors == null || type.Constructors.Count == 0) { return; }
        ResolvedConstructor ctor = type.Constructors[0];
        SerializableTypeMetadata metadata = CreateConstrMetadata(ns, type.TypeName, ctor.Index, ctor.Fields);
        Emitter.EmitSerializerAndMetadata(context, metadata);
        _ = emittedLocalTypes.Add(type.TypeName);
        registrations[metadata.FullyQualifiedName] = new RegistrationEntry(metadata.FullyQualifiedName, IsValueType: false);
    }

    private static void EmitUnion(SourceProductionContext context, string ns, ResolvedType type, HashSet<string> emittedLocalTypes, Dictionary<string, RegistrationEntry> registrations)
    {
        if (type.Constructors == null) { return; }
        string interfaceName = "I" + type.TypeName;
        List<SerializableTypeMetadata> childMetadatas = [];
        foreach (ResolvedConstructor ctor in type.Constructors)
        {
            SerializableTypeMetadata childMeta = CreateConstrMetadata(ns, ctor.Name, ctor.Index, ctor.Fields);
            childMetadatas.Add(childMeta);
            Emitter.EmitSerializerAndMetadata(context, childMeta);
            _ = emittedLocalTypes.Add(ctor.Name);
            registrations[childMeta.FullyQualifiedName] = new RegistrationEntry(childMeta.FullyQualifiedName, IsValueType: false);
        }
        SerializableTypeMetadata unionMeta = new(interfaceName, interfaceName, ns, null, $"global::{ns}.{interfaceName}", "interface", null, null, false, false, null, SerializationType.Union, null, true) { ChildTypes = new(childMetadatas) };
        Emitter.EmitSerializerAndMetadata(context, unionMeta);
        _ = emittedLocalTypes.Add(interfaceName);
        registrations[unionMeta.FullyQualifiedName] = new RegistrationEntry(unionMeta.FullyQualifiedName, IsValueType: false);
    }

    private static void EmitTuple(SourceProductionContext context, string ns, ResolvedType type, HashSet<string> emittedLocalTypes, Dictionary<string, RegistrationEntry> registrations)
    {
        if (type.TupleFields == null || type.TupleFields.Count == 0) { return; }
        SerializableTypeMetadata metadata = new(type.TypeName, type.TypeName, ns, null, $"global::{ns}.{type.TypeName}", "record", null, null, type.TupleFields.Count > 0, false, null, SerializationType.List, null, true) { Properties = new(type.TupleFields.Select(ConvertField)) };
        Emitter.EmitSerializerAndMetadata(context, metadata);
        _ = emittedLocalTypes.Add(type.TypeName);
        registrations[metadata.FullyQualifiedName] = new RegistrationEntry(metadata.FullyQualifiedName, IsValueType: false);
    }

    private static SerializableTypeMetadata CreateConstrMetadata(
        string ns, string typeName, int constrIndex,
        List<ResolvedField> fields) =>
        new(
            BaseIdentifier: typeName,
            Indentifier: typeName,
            Namespace: ns,
            TypeParams: null,
            FullyQualifiedName: $"global::{ns}.{typeName}",
            Keyword: "record",
            CborTag: null,
            CborIndex: constrIndex,
            IsIndefinite: fields.Count > 0,
            IsDefinite: false,
            DefiniteSize: null,
            SerializationType: SerializationType.Constr,
            Validator: null,
            ShouldPreserveRaw: true
        )
        {
            Properties = new(fields.Select(ConvertField))
        };

    private static SerializablePropertyMetadata ConvertField(ResolvedField field)
    {
        string csharpType = field.CSharpType;
        string fqn = ResolveFullyQualifiedName(csharpType);
        string typeNs = ExtractNamespace(fqn);
        bool isNullable = field.IsNullable || csharpType.EndsWith("?", StringComparison.Ordinal);
        string baseType = isNullable && csharpType.EndsWith("?", StringComparison.Ordinal)
            ? csharpType.TrimEnd('?') : csharpType;

        // Use FQN for propertyType so generated serializer code resolves all types
        string propertyType = fqn + (isNullable ? "?" : "");

        return new SerializablePropertyMetadata(
            PropertyName: field.Name,
            PropertyType: propertyType,
            PropertyTypeFullName: fqn,
            PropertyTypeNamespace: typeNs,
            IsList: false,
            IsMap: false,
            ListItemType: null,
            ListItemTypeFullName: null,
            ListItemTypeNamespace: null,
            IsListItemTypeOpenGeneric: false,
            IsMapKeyTypeOpenGeneric: false,
            IsMapValueTypeOpenGeneric: false,
            MapKeyType: null,
            MapValueType: null,
            MapKeyTypeFullName: null,
            MapValueTypeFullName: null,
            MapKeyTypeNamespace: null,
            MapValueTypeNamespace: null,
            IsNullable: isNullable,
            Size: null,
            IsIndefinite: false,
            IsDefinite: false,
            Order: field.Order,
            PropertyKeyString: null,
            PropertyKeyInt: null,
            IsOpenGeneric: false,
            IsPropertyTypeUnion: IsUnionType(baseType)
        );
    }

    private static bool IsUnionType(string typeName) =>
        typeName.StartsWith("I", StringComparison.Ordinal) &&
        typeName.Length > 1 &&
        char.IsUpper(typeName[1]) &&
        !typeName.StartsWith("ICborOption<", StringComparison.Ordinal);

    private static bool TryResolveGlobalRegistrationName(string csharpType, string ns, HashSet<string> emittedLocalTypes, out string globalFqn)
    {
        string baseType = csharpType.TrimEnd('?');
        int genericStart = baseType.IndexOf('<');
        if (genericStart > 0 && baseType.EndsWith(">", StringComparison.Ordinal))
        {
            string resolvedOuter = ResolveSimpleType(baseType.Substring(0, genericStart));
            if (resolvedOuter.StartsWith("global::", StringComparison.Ordinal)
                && TryResolveGlobalRegistrationName(baseType.Substring(genericStart + 1, baseType.Length - genericStart - 2), ns, emittedLocalTypes, out string resolvedInner))
            {
                globalFqn = $"{resolvedOuter}<{resolvedInner}>";
                return true;
            }
            globalFqn = string.Empty;
            return false;
        }
        string resolved = ResolveSimpleType(baseType);
        if (resolved.StartsWith("global::", StringComparison.Ordinal)) { globalFqn = resolved; return true; }
        if (emittedLocalTypes.Contains(resolved)) { globalFqn = $"global::{ns}.{resolved}"; return true; }
        globalFqn = string.Empty;
        return false;
    }

    private static string ResolveFullyQualifiedName(string csharpType)
    {
        string baseType = csharpType.TrimEnd('?');

        // Handle generic types: resolve outer and inner type arguments
        int genericStart = baseType.IndexOf('<');
        if (genericStart > 0 && baseType.EndsWith(">", StringComparison.Ordinal))
        {
            string outer = baseType.Substring(0, genericStart);
            string inner = baseType.Substring(genericStart + 1, baseType.Length - genericStart - 2);
            string resolvedOuter = ResolveSimpleType(outer);
            string resolvedInner = ResolveFullyQualifiedName(inner);
            return $"{resolvedOuter}<{resolvedInner}>";
        }

        return ResolveSimpleType(baseType);
    }

    private static string ResolveSimpleType(string typeName) => typeName switch
    {
        "IPlutusBigInt" => "global::Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusBigInt",
        "IPlutusBool" => "global::Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusBool",
        "IPlutusData" => "global::Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusData",
        "PlutusBoundedBytes" => "global::Chrysalis.Codec.Types.Cardano.Core.Common.PlutusBoundedBytes",
        "ICborOption" => "global::Chrysalis.Codec.Types.ICborOption",
        "CborList" => "global::Chrysalis.Codec.Types.CborList",
        _ => typeName,
    };

    private static string ExtractNamespace(string fqn)
    {
        if (fqn.StartsWith("global::", StringComparison.Ordinal))
        {
            fqn = fqn.Substring("global::".Length);
        }

        int lastDot = fqn.LastIndexOf('.');
        return lastDot > 0 ? fqn.Substring(0, lastDot) : "";
    }
}

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
    public static void EmitSerializers(
        SourceProductionContext context,
        string ns,
        Dictionary<string, ResolvedType> resolvedTypes)
    {
        foreach (KeyValuePair<string, ResolvedType> kv in resolvedTypes)
        {
            ResolvedType type = kv.Value;
            switch (type.Category)
            {
                case TypeCategory.SingleConstructor:
                    EmitSingleConstructor(context, ns, type);
                    break;
                case TypeCategory.Union:
                    EmitUnion(context, ns, type);
                    break;
                case TypeCategory.Tuple:
                    EmitTuple(context, ns, type);
                    break;
                case TypeCategory.PrimitiveBytes:
                case TypeCategory.PrimitiveInteger:
                case TypeCategory.PrimitiveBool:
                case TypeCategory.PrimitiveData:
                case TypeCategory.Option:
                case TypeCategory.List:
                case TypeCategory.Ref:
                default:
                    break;
            }
        }
    }

    private static void EmitSingleConstructor(SourceProductionContext context, string ns, ResolvedType type)
    {
        if (type.Constructors == null || type.Constructors.Count == 0)
        {
            return;
        }

        ResolvedConstructor ctor = type.Constructors[0];
        SerializableTypeMetadata metadata = CreateConstrMetadata(ns, type.TypeName, ctor.Index, ctor.Fields);
        Emitter.EmitSerializerAndMetadata(context, metadata);
    }

    private static void EmitUnion(SourceProductionContext context, string ns, ResolvedType type)
    {
        if (type.Constructors == null)
        {
            return;
        }

        string interfaceName = "I" + type.TypeName;

        // Emit serializer for each variant
        List<SerializableTypeMetadata> childMetadatas = [];
        foreach (ResolvedConstructor ctor in type.Constructors)
        {
            SerializableTypeMetadata childMeta = CreateConstrMetadata(ns, ctor.Name, ctor.Index, ctor.Fields);
            childMetadatas.Add(childMeta);
            Emitter.EmitSerializerAndMetadata(context, childMeta);
        }

        // Emit union interface serializer
        SerializableTypeMetadata unionMeta = new(
            baseIdentifier: interfaceName,
            identifier: interfaceName,
            @namespace: ns,
            typeParams: null,
            fullyQualifiedName: $"global::{ns}.{interfaceName}",
            keyword: "interface",
            cborTag: null,
            cborIndex: null,
            isIndefinite: false,
            isDefinite: false,
            definiteSize: null,
            serializationType: SerializationType.Union,
            validator: null,
            shouldPreserveRaw: true
        );

        foreach (SerializableTypeMetadata child in childMetadatas)
        {
            unionMeta.ChildTypes.Add(child);
        }

        Emitter.EmitSerializerAndMetadata(context, unionMeta);
    }

    private static void EmitTuple(SourceProductionContext context, string ns, ResolvedType type)
    {
        if (type.TupleFields == null || type.TupleFields.Count == 0)
        {
            return;
        }

        SerializableTypeMetadata metadata = new(
            baseIdentifier: type.TypeName,
            identifier: type.TypeName,
            @namespace: ns,
            typeParams: null,
            fullyQualifiedName: $"global::{ns}.{type.TypeName}",
            keyword: "record",
            cborTag: null,
            cborIndex: null,
            isIndefinite: type.TupleFields.Count > 0,
            isDefinite: false,
            definiteSize: null,
            serializationType: SerializationType.List,
            validator: null,
            shouldPreserveRaw: true
        );

        foreach (ResolvedField field in type.TupleFields)
        {
            metadata.Properties.Add(ConvertField(field));
        }

        Emitter.EmitSerializerAndMetadata(context, metadata);
    }

    private static SerializableTypeMetadata CreateConstrMetadata(
        string ns, string typeName, int constrIndex,
        List<ResolvedField> fields)
    {
        SerializableTypeMetadata metadata = new(
            baseIdentifier: typeName,
            identifier: typeName,
            @namespace: ns,
            typeParams: null,
            fullyQualifiedName: $"global::{ns}.{typeName}",
            keyword: "record",
            cborTag: null,
            cborIndex: constrIndex,
            isIndefinite: fields.Count > 0,
            isDefinite: false,
            definiteSize: null,
            serializationType: SerializationType.Constr,
            validator: null,
            shouldPreserveRaw: true
        );

        foreach (ResolvedField field in fields)
        {
            metadata.Properties.Add(ConvertField(field));
        }

        return metadata;
    }

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
            propertyName: field.Name,
            propertyType: propertyType,
            propertyTypeFullName: fqn,
            propertyTypeNamespace: typeNs,
            isList: false,
            isMap: false,
            listItemType: null,
            listItemTypeFullName: null,
            listItemTypeNamespace: null,
            isListItemTypeOpenGeneric: false,
            isMapKeyTypeOpenGeneric: false,
            isMapValueTypeOpenGeneric: false,
            mapKeyType: null,
            mapValueType: null,
            mapKeyTypeFullName: null,
            mapValueTypeFullName: null,
            mapKeyTypeNamespace: null,
            mapValueTypeNamespace: null,
            isNullable: isNullable,
            size: null,
            isIndefinite: false,
            isDefinite: false,
            order: field.Order,
            propertyKeyString: null,
            propertyKeyInt: null,
            isOpenGeneric: false,
            isPropertyTypeUnion: IsUnionType(baseType)
        );
    }

    private static bool IsUnionType(string typeName) =>
        typeName.StartsWith("I", StringComparison.Ordinal) &&
        typeName.Length > 1 &&
        char.IsUpper(typeName[1]) &&
        !typeName.StartsWith("ICborOption<", StringComparison.Ordinal) &&
        !typeName.StartsWith("ICborMaybeIndefList<", StringComparison.Ordinal);

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
        "CborDefList" => "global::Chrysalis.Codec.Types.CborDefList",
        "CborIndefList" => "global::Chrysalis.Codec.Types.CborIndefList",
        "ICborMaybeIndefList" => "global::Chrysalis.Codec.Types.ICborMaybeIndefList",
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

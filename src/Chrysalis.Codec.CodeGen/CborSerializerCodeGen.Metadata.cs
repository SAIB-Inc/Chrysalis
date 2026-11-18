namespace Chrysalis.Codec.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    internal enum SerializationType
    {
        Container,
        Map,
        List,
        Constr,
        Union
    }

    internal sealed record SerializableTypeMetadata(
        string BaseIdentifier,
        string Indentifier,
        string? Namespace,
        string? TypeParams,
        string FullyQualifiedName,
        string Keyword,
        int? CborTag,
        int? CborIndex,
        bool IsIndefinite,
        bool IsDefinite,
        int? DefiniteSize,
        SerializationType SerializationType,
        string? Validator,
        bool ShouldPreserveRaw
    )
    {
        public bool IsRecordStruct => Keyword.Contains("struct");
        public bool IsTypeNullable => Indentifier.Contains("?");

        public bool KeepNetworkReadTrial { get; init; }
        public EquatableArray<SerializableTypeMetadata> ChildTypes { get; init; }
        public EquatableArray<SerializablePropertyMetadata> Properties { get; init; }

        public override string ToString() => $$"""
                // Keyword: {{Keyword}}
                // Identifier: {{Indentifier}}
                // Namespace: {{Namespace}}
                // TypeParams: {{TypeParams}}
                // FullyQualifiedName: {{FullyQualifiedName}}
                // SerializationType: {{SerializationType}}
                // CborTag: {{CborTag}}
                // CborIndex: {{CborIndex}}
                // IsIndefinite: {{IsIndefinite}}
                // IsDefinite: {{IsDefinite}}
                // ShouldPreserveRaw: {{ShouldPreserveRaw}}
                // Validator: {{Validator}}
                // Properties: {{string.Join(",", Properties.Select(p => p.ToString()))}}
                // ChildTypes: {{string.Join(",", ChildTypes.Select(t => t.ToString()))}}
                // IsTypeNullable: {{IsTypeNullable}}
            """;
    }

    internal sealed record SerializablePropertyMetadata(
        string PropertyName,
        string PropertyType,
        string PropertyTypeFullName,
        string PropertyTypeNamespace,
        bool IsList,
        bool IsMap,
        string? ListItemType,
        string? ListItemTypeFullName,
        string? ListItemTypeNamespace,
        bool IsListItemTypeOpenGeneric,
        bool IsMapKeyTypeOpenGeneric,
        bool IsMapValueTypeOpenGeneric,
        string? MapKeyType,
        string? MapValueType,
        string? MapKeyTypeFullName,
        string? MapValueTypeFullName,
        string? MapKeyTypeNamespace,
        string? MapValueTypeNamespace,
        bool IsNullable,
        int? Size,
        bool IsIndefinite,
        bool IsDefinite,
        int? Order,
        string? PropertyKeyString,
        int? PropertyKeyInt,
        bool IsOpenGeneric,
        bool IsPropertyTypeUnion = false,
        bool IsListItemTypeUnion = false,
        bool IsMapKeyTypeUnion = false,
        bool IsMapValueTypeUnion = false,
        string? CborMaybeIndefListItemTypeFullName = null,
        bool IsPairList = false
    )
    {
        public bool IsTypeNullable => PropertyType.Contains("?");
        public bool IsRequired => !IsTypeNullable && !IsNullable;


        // Default ties preserve wire-list order for X7 signatures. Split records may differ.
        public int CreateParameterOrder { get; init; }
        public EquatableArray<KeyValuePair<int, string>> UnionHints { get; init; }
        public string? UnionHintDiscriminantProperty { get; init; }

        public override string ToString() => $$"""
                // PropertyName: {{PropertyName}}
                // PropertyType: {{PropertyType}}
                // PropertyTypeFullName: {{PropertyTypeFullName}}
                // PropertyTypeNamespace: {{PropertyTypeNamespace}}
                // IsNullable: {{IsNullable}}
                // Size: {{Size}}
                // IsIndefinite: {{IsIndefinite}}
                // Order: {{Order}}
                // PropertyKeyString: {{PropertyKeyString}}
                // PropertyKeyInt: {{PropertyKeyInt}}
                // IsList: {{IsList}}
                // IsMap: {{IsMap}}
                // ListItemType: {{ListItemType}}
                // ListItemTypeFullName: {{ListItemTypeFullName}}
                // IsListItemTypeOpenGeneric: {{IsListItemTypeOpenGeneric}}
                // MapKeyType: {{MapKeyType}}
                // MapValueType: {{MapValueType}}
                // MapKeyTypeFullName: {{MapKeyTypeFullName}}
                // MapValueTypeFullName: {{MapValueTypeFullName}}
                // MapKeyTypeNamespace: {{MapKeyTypeNamespace}}
                // MapValueTypeNamespace: {{MapValueTypeNamespace}}
                // IsMapKeyTypeOpenGeneric: {{IsMapKeyTypeOpenGeneric}}
                // IsMapValueTypeOpenGeneric: {{IsMapValueTypeOpenGeneric}}
                // IsOpenGeneric: {{IsOpenGeneric}}
                // IsTypeNullable: {{IsTypeNullable}}
            """;
    }
}

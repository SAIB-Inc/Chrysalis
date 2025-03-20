namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private enum SerializationType
    {
        Container,
        Map,
        List,
        Constr,
        Union
    }

    private sealed class SerializableTypeMetadata(
        string identifier,
        string? @namespace,
        string keyword,
        int? cborTag,
        int? cborIndex,
        SerializationType serializationType,
        string? validator,
        bool shouldPreserveRaw
    )
    {
        public string Indentifier { get; } = identifier;
        public string? Namespace { get; } = @namespace;
        public string FullyQualifiedName { get; } = string.IsNullOrEmpty(@namespace) ? identifier : $"{@namespace}.{identifier}";
        public string Keyword { get; } = keyword;
        public int? CborTag { get; } = cborTag;
        public int? CborIndex { get; } = cborIndex;
        public SerializationType SerializationType { get; } = serializationType;
        public string? Validator { get; } = validator;
        public bool ShouldPreserveRaw { get; } = shouldPreserveRaw;

        public List<SerializableTypeMetadata> ChildTypes { get; } = [];
        public List<SerializablePropertyMetadata> Properties { get; } = [];

        public override string ToString()
        {
            // better formatted strring
            return $$"""
                // Keyword: {{Keyword}}
                // Identifier: {{Indentifier}}
                // Namespace: {{Namespace}}
                // FullyQualifiedName: {{FullyQualifiedName}}
                // SerializationType: {{SerializationType}}
                // CborTag: {{CborTag}}
                // CborIndex: {{CborIndex}}
                // ShouldPreserveRaw: {{ShouldPreserveRaw}}
                // Validator: {{Validator}}
                // Properties: {{string.Join(",", Properties.Select(p => p.ToString()))}}
                // ChildTypes: {{string.Join(",", ChildTypes.Select(t => t.ToString()))}}
            """;
        }
    }

    private sealed class SerializablePropertyMetadata(
        string propertyName,
        string propertyType,
        string propertyTypeFullName,
        string propertyTypeNamespace,
        bool isList,
        bool isMap,
        string? listItemType,
        string? listItemTypeFullName,
        string? listItemTypeNamespace,
        bool isListItemTypeOpenGeneric,
        bool isMapKeyTypeOpenGeneric,
        bool isMapValueTypeOpenGeneric,
        string? mapKeyType,
        string? mapValueType,
        string? mapKeyTypeFullName,
        string? mapValueTypeFullName,
        string? mapKeyTypeNamespace,
        string? mapValueTypeNamespace,
        bool isNullable,
        int? size,
        bool isIndefinite,
        int? order,
        string? propertyKeyString,
        int? propertyKeyInt
    )
    {
        public string PropertyName { get; } = propertyName;
        public string PropertyType { get; } = propertyType;
        public string PropertyTypeFullName { get; } = propertyTypeFullName;
        public string PropertyTypeNamespace { get; } = propertyTypeNamespace;
        public bool IsList { get; } = isList;
        public bool IsMap { get; } = isMap;

        public string? ListItemType { get; } = listItemType;
        public bool IsListItemTypeOpenGeneric { get; } = isListItemTypeOpenGeneric;
        public string? ListItemTypeFullName { get; } = listItemTypeFullName;
        public string? ListItemTypeNamespace { get; } = listItemTypeNamespace;
        public string? MapKeyType { get; } = mapKeyType;
        public bool IsMapKeyTypeOpenGeneric { get; } = isMapKeyTypeOpenGeneric;
        public string? MapValueType { get; } = mapValueType;
        public bool IsMapValueTypeOpenGeneric { get; } = isMapValueTypeOpenGeneric;
        public string? MapKeyTypeFullName { get; } = mapKeyTypeFullName;
        public string? MapValueTypeFullName { get; } = mapValueTypeFullName;
        public string? MapKeyTypeNamespace { get; } = mapKeyTypeNamespace;
        public string? MapValueTypeNamespace { get; } = mapValueTypeNamespace;

        // Attributes
        public bool IsNullable { get; } = isNullable;
        public int? Size { get; } = size;
        public bool IsIndefinite { get; } = isIndefinite;
        public int? Order { get; set; } = order;
        public string? PropertyKeyString { get; } = propertyKeyString;
        public int? PropertyKeyInt { get; } = propertyKeyInt;

        public override string ToString()
        {
            return $$"""
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
            """;
        }
    }
}
namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private enum SerializationType
    {
        Container,
        Map,
        Array,
        Constr,
        Union
    }

    private sealed class SerializableTypeMetadata(
        string typeName,
        string @namespace,
        string fullName,
        string typeDeclaration,
        int? cborTag,
        int? cborIndex,
        SerializationType serializationType,
        string? validator
    )
    {
        public string TypeName { get; } = typeName;
        public string Namespace { get; } = @namespace;
        public string FullName { get; } = fullName;
        public string TypeDeclaration { get; } = typeDeclaration;
        public int? CborTag { get; set; } = cborTag;
        public int? CborIndex { get; set; } = cborIndex;
        public SerializationType SerializationType { get; } = serializationType;
        public string? Validator { get; set; } = validator;

        public List<SerializableTypeMetadata> ChildTypes { get; } = [];
        public List<SerializablePropertyMetadata> Properties { get; } = [];
        public Dictionary<int, SerializablePropertyMetadata> PropertyOrderMapping { get; } = [];
        public Dictionary<string, SerializablePropertyMetadata> PropertyStringKeyMapping { get; } = [];
        public Dictionary<string, SerializablePropertyMetadata> PropertyIndexKeyMapping { get; } = [];
    }

    private sealed class SerializablePropertyMetadata(
        string propertyName,
        string propertyType,
        string propertyTypeFullName,
        string propertyTypeNamespace,
        bool isNullable,
        int? size,
        bool isIndefinite)
    {
        public string PropertyName { get; } = propertyName;
        public string PropertyType { get; } = propertyType;
        public string PropertyTypeFullName { get; } = propertyTypeFullName;
        public string PropertyTypeNamespace { get; } = propertyTypeNamespace;

        // Attributes
        public bool IsNullable { get; } = isNullable;
        public int? Size { get; } = size;
        public bool IsIndefinite { get; } = isIndefinite;
        public int? Order { get; set; }
        public string? PropertyKeyString { get; set; }
        public int? PropertyKeyInt { get; set; }
        
        // Validation attributes
        public object? ExpectedValue { get; set; }
        public double? MinimumValue { get; set; }
        public double? MaximumValue { get; set; }
        public string? ValidatorTypeName { get; set; }
    }
}
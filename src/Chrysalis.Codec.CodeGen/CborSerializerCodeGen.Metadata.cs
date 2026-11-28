using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    internal readonly record struct RegistrationEntry(string FullyQualifiedName, bool IsValueType);

    internal enum SerializationType
    {
        Container,
        Map,
        List,
        Constr,
        Union
    }

    internal enum UnionReaderKind
    {
        Generated,
        Declared,
        Invalid
    }

    internal sealed record StructUnionCaseRef(
        string SymbolKey,
        string BoundTypeFullName,
        string BoundIdentifier,
        string BaseIdentifier,
        bool IsValueType,
        SerializableTypeMetadata? BoundMetadata);

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
        public bool IsRecordStruct => Keyword.Contains("record struct");
        public bool IsStructUnion => SerializationType == SerializationType.Union && Keyword == "readonly struct";
        public bool IsTypeNullable => Indentifier.Contains("?");

        public UnionReaderKind UnionReader { get; init; }
        public DiagnosticDescriptor? UnionError { get; init; }
        public string? UnionErrorDetail { get; init; }
        public bool HasAmbiguousExternalWireOrder { get; init; }
        public bool IsValueType { get; init; }
        public bool IsAbstract { get; init; }
        public bool HasSerializableReadWrite { get; init; }
        public bool IsAccessibleConcrete { get; init; }
        public string? GlobalFullyQualifiedName { get; init; }
        public string? OriginalDefinitionKey { get; init; }
        public bool IsOpenGenericType => !string.IsNullOrEmpty(TypeParams);
        public bool CanImplementCodecRegistrar => IsOpenGenericType && Keyword != "interface" && !IsAbstract && UnionReader != UnionReaderKind.Invalid;
        public EquatableArray<StructUnionCaseRef> StructUnionCases { get; init; }
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

    internal sealed record CborListPropertyMetadata(string Type, bool IsNullable);
    internal sealed record CborMapPropertyMetadata(string Type, bool IsNullable);

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
        CborListPropertyMetadata? CborList = null,
        bool IsPairList = false,
        CborMapPropertyMetadata? CborMap = null
    )
    {
        public bool IsTypeNullable =>
            CborList?.IsNullable ?? CborMap?.IsNullable
            ?? (PropertyType.TrimEnd().EndsWith("?", StringComparison.Ordinal)
                || PropertyTypeFullName.TrimEnd().EndsWith("?", StringComparison.Ordinal)
                || PropertyTypeFullName.StartsWith("global::System.Nullable<", StringComparison.Ordinal));
        public string? TypeofTypeFullName { get; init; }
        public string TypeofType => TypeofTypeFullName ?? StripOuterNullability(NonNullableTypeFullName);
        public string NonNullableTypeFullName => CborList?.Type ?? CborMap?.Type ?? StripOuterNullability(PropertyTypeFullName);
        public bool IsRequired => !IsTypeNullable && !IsNullable;


        // Default ties preserve wire-list order for X7 signatures. Split records may differ.
        public int CreateParameterOrder { get; init; }
        public bool IsLedgerText { get; init; }
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

    internal static string StripOuterNullability(string type)
    {
        string trimmed = type.Trim();
        if (trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            return trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
        }

        return UnwrapNullable(trimmed, "global::System.Nullable<")
            ?? UnwrapNullable(trimmed, "System.Nullable<")
            ?? UnwrapNullable(trimmed, "Nullable<")
            ?? trimmed;

        static string? UnwrapNullable(string value, string prefix) =>
            value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(">", StringComparison.Ordinal)
                ? value.Substring(prefix.Length, value.Length - prefix.Length - 1).Trim()
                : null;
    }
}

namespace Chrysalis.Codec.V2.Serialization;

public sealed record CborOptions(
    int? Index,
    bool IsDefinite,
    int? Tag,
    int? Size,
    Type? ObjectType,
    Type? NormalizedType,
    Type? ConverterType,
    Type? RuntimeType,
    Dictionary<int, string>? IndexPropertyMapping,
    Dictionary<string, string>? NamedPropertyMapping,
    IEnumerable<Type>? UnionTypes
);

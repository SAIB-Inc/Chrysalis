using System.Reflection;

namespace Chrysalis.Cbor.Serialization;

public partial record CborOptions
{
    public static readonly CborOptions Default = new();

    public int Index { get; init; } = -1;
    public bool IsDefinite { get; init; } = false;
    public int? Tag { get; init; } = -1;
    public int? Size { get; init; } = -1;
    public Type? ObjectType { get; }
    public Type? NormalizedType { get; }
    public Type? ConverterType { get; }
    public Type? RuntimeType { get; set; }
    public object? ExactValue { get; set; }
    public ConstructorInfo? Constructor { get; set; }
    public IReadOnlyDictionary<int, (Type Type, object? ExpectedValue)>? IndexPropertyMapping { get; }
    public IReadOnlyDictionary<string, (Type Type, object? ExpectedValue)>? NamedPropertyMapping { get; }
    public IReadOnlyCollection<Type>? UnionTypes { get; }

    public CborOptions(
        int index = -1,
        bool isDefinite = false,
        int tag = -1,
        int size = -1,
        Type? objectType = null,
        Type? normalizedType = null,
        Type? converterType = null,
        IReadOnlyDictionary<int, (Type Type, object? ExpectedValue)>? indexPropertyMapping = null,
        IReadOnlyDictionary<string, (Type Type, object? ExpectedValue)>? namedPropertyMapping = null,
        IReadOnlyCollection<Type>? unionTypes = null,
        ConstructorInfo? constructor = null
    )
    {
        Index = index;
        IsDefinite = isDefinite;
        Tag = tag;
        Size = size;
        ObjectType = objectType;
        NormalizedType = normalizedType;
        ConverterType = converterType;
        IndexPropertyMapping = indexPropertyMapping;
        NamedPropertyMapping = namedPropertyMapping;
        UnionTypes = unionTypes;
        Constructor = constructor;
    }
}
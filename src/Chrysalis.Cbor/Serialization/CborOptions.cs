using System.Reflection;

namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Configuration options for CBOR serialization and deserialization.
/// </summary>
public partial record CborOptions
{
    /// <summary>
    /// Gets the default CBOR options instance.
    /// </summary>
    public static readonly CborOptions Default = new();

    /// <summary>
    /// Gets the property index for ordered serialization.
    /// </summary>
    public int Index { get; init; } = -1;

    /// <summary>
    /// Gets a value indicating whether the CBOR encoding uses definite length.
    /// </summary>
    public bool IsDefinite { get; init; }

    /// <summary>
    /// Gets the CBOR tag value, or -1 if no tag is specified.
    /// </summary>
    public int? Tag { get; init; } = -1;

    /// <summary>
    /// Gets the size constraint, or -1 if no size is specified.
    /// </summary>
    public int? Size { get; init; } = -1;

    /// <summary>
    /// Gets the object type for serialization.
    /// </summary>
    public Type? ObjectType { get; }

    /// <summary>
    /// Gets the normalized type after generic resolution.
    /// </summary>
    public Type? NormalizedType { get; }

    /// <summary>
    /// Gets the converter type used for serialization.
    /// </summary>
    public Type? ConverterType { get; }

    /// <summary>
    /// Gets or sets the runtime type resolved during deserialization.
    /// </summary>
    public Type? RuntimeType { get; set; }

    /// <summary>
    /// Gets or sets an exact value constraint for matching.
    /// </summary>
    public object? ExactValue { get; set; }

    /// <summary>
    /// Gets or sets the constructor info used for object creation.
    /// </summary>
    public ConstructorInfo? Constructor { get; set; }

    /// <summary>
    /// Gets the mapping of integer indices to property types and expected values.
    /// </summary>
    public IReadOnlyDictionary<int, (Type Type, object? ExpectedValue)>? IndexPropertyMapping { get; }

    /// <summary>
    /// Gets the mapping of property names to types and expected values.
    /// </summary>
    public IReadOnlyDictionary<string, (Type Type, object? ExpectedValue)>? NamedPropertyMapping { get; }

    /// <summary>
    /// Gets the collection of union types for polymorphic deserialization.
    /// </summary>
    public IReadOnlyCollection<Type>? UnionTypes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CborOptions"/> record.
    /// </summary>
    /// <param name="index">The property index for ordered serialization.</param>
    /// <param name="isDefinite">Whether the CBOR encoding uses definite length.</param>
    /// <param name="tag">The CBOR tag value.</param>
    /// <param name="size">The size constraint.</param>
    /// <param name="objectType">The object type for serialization.</param>
    /// <param name="normalizedType">The normalized type after generic resolution.</param>
    /// <param name="converterType">The converter type used for serialization.</param>
    /// <param name="indexPropertyMapping">The mapping of integer indices to property types.</param>
    /// <param name="namedPropertyMapping">The mapping of property names to types.</param>
    /// <param name="unionTypes">The collection of union types.</param>
    /// <param name="constructor">The constructor info used for object creation.</param>
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

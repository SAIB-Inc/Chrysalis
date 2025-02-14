using Chrysalis.Cbor.Converters;

namespace Chrysalis.Cbor.Types;

public record CborOptions
{

    public int? Index { get; set; }
    public Type? ConverterType { get; set; }
    public bool? IsDefinite { get; set; }
    public bool? IsUnion { get; set; }
    public Type? ActivatorType { get; set; }
    public int? Size { get; set; }
    public int? Tag { get; set; }
    public Dictionary<string, Type>? PropertyNameTypes { get; set; }
    public Dictionary<int, Type>? PropertyIndexTypes { get; set; }
    public IEnumerable<Type>? UnionTypes { get; set; }

    public CborOptions(
        int? Index = 0,
        Type? ConverterType = null,
        bool? IsDefinite = null,
        bool? IsUnion = false,
        Type? ActivatorType = null,
        int? Size = null,
        int? Tag = null,
        Dictionary<string, Type>? PropertyNameTypes = null,
        Dictionary<int, Type>? PropertyIndexTypes = null,
        IEnumerable<Type>? UnionTypes = null)
    {
        this.Index = Index;
        this.ConverterType = ConverterType;
        this.IsDefinite = IsDefinite;
        this.IsUnion = IsUnion;
        this.ActivatorType = ActivatorType;
        this.Size = Size;
        this.Tag = Tag;
        this.PropertyNameTypes = PropertyNameTypes;
        this.PropertyIndexTypes = PropertyIndexTypes;
        this.UnionTypes = UnionTypes;
    }
}
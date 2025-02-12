using Chrysalis.Cbor.Converters;

namespace Chrysalis.Cbor.Types;

public record CborOptions(
    int? Index = 0,
    Type? ConverterType = null,
    bool? IsDefinite = null,
    int? Size = null,
    int? Tag = null,
    IEnumerable<string>? PropertyNames = null,
    IEnumerable<int>? PropertyIndices = null
);
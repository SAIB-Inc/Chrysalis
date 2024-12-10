using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

[CborConverter(typeof(UnionConverter))]
public abstract record DatumOption : CborBase;


[CborConverter(typeof(CustomListConverter))]
public record DatumHashOption(
    [CborProperty(0)] CborInt Option,
    [CborProperty(1)] CborBytes DatumHash
) : DatumOption;


[CborConverter(typeof(CustomListConverter))]
public record InlineDatumOption(
    [CborProperty(0)] CborInt Option,
    [CborProperty(1)] CborEncodedValue Data
) : DatumOption;
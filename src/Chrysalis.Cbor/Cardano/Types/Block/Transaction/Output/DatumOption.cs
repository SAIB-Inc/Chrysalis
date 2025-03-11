using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborConverter(typeof(UnionConverter))]
public abstract partial record DatumOption : CborBase;


[CborConverter(typeof(CustomListConverter))]
public partial record DatumHashOption(
    [CborIndex(0)] CborInt Option,
    [CborIndex(1)] CborBytes DatumHash
) : DatumOption;


[CborConverter(typeof(CustomListConverter))]
public partial record InlineDatumOption(
    [CborIndex(0)] CborInt Option,
    [CborIndex(1)] CborEncodedValue Data
) : DatumOption;
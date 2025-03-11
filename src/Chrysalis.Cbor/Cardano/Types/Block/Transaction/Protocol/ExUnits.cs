using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborConverter(typeof(CustomListConverter))]
public partial record ExUnits(
    [CborIndex(0)] CborUlong Mem,
    [CborIndex(1)] CborUlong Steps
) : CborBase;

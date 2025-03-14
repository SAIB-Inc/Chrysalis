using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

// [CborSerializable]
[CborList]
public partial record ExUnits(
    [CborIndex(0)] ulong Mem,
    [CborIndex(1)] ulong Steps
) : CborBase<ExUnits>;

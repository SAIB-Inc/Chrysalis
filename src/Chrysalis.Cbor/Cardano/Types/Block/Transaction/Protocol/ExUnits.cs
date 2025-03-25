using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborSerializable]
[CborList]
public partial record ExUnits(
    [CborOrder(0)] ulong Mem,
    [CborOrder(1)] ulong Steps
) : CborBase;

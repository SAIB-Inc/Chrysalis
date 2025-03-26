using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborList]
public partial record ExUnits(
    [CborOrder(0)] ulong Mem,
    [CborOrder(1)] ulong Steps
) : CborBase;

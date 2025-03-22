using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block;

[CborSerializable]
[CborList]
public partial record BlockWithEra(
    [CborOrder(0)] int EraNumber,
    [CborOrder(1)] Block Block
) : CborBase<BlockWithEra>;
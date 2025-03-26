using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core;

[CborSerializable]
[CborList]
public partial record BlockWithEra(
    [CborOrder(0)] int EraNumber,
    [CborOrder(1)] Block Block
) : CborBase, ICborPreserveRaw;
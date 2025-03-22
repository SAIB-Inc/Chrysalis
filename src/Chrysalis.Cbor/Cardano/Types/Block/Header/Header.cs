using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header;

[CborSerializable]
[CborList]
public partial record BlockHeader(
    [CborOrder(0)] BlockHeaderBody HeaderBody,
    [CborOrder(1)] byte[] BodySignature
) : CborBase<BlockHeader>, ICborPreserveRaw;

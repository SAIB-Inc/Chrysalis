using Chrysalis.Cbor.Attributes;

using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header;

[CborSerializable]
[CborList]
public partial record BlockHeader(
    [CborIndex(0)] BlockHeaderBody HeaderBody,
    [CborIndex(1)] byte[] BodySignature
) : CborBase<BlockHeader>;

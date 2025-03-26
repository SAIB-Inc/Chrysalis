using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Types.Cardano.Core.Header;

[CborSerializable]
[CborList]
public partial record BlockHeader(
    [CborOrder(0)] BlockHeaderBody HeaderBody,
    [CborOrder(1)] byte[] BodySignature
) : CborBase, ICborPreserveRaw;

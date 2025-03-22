using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

[CborSerializable]
[CborList]
public partial record VrfCert(
    [CborOrder(0)] byte[] Proof,
    [CborOrder(1)] byte[] Output
) : CborBase<VrfCert>;
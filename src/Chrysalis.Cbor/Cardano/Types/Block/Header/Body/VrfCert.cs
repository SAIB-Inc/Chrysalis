using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

[CborSerializable]
[CborList]
public partial record VrfCert(
    [CborIndex(0)] byte[] Proof,
    [CborIndex(1)] byte[] Output
) : CborBase<VrfCert>;
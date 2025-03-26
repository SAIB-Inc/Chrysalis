using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Header;

[CborSerializable]
[CborList]
public partial record VrfCert(
    [CborOrder(0)] byte[] Proof,
    [CborOrder(1)] byte[] Output
) : CborBase;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Header;

/// <summary>
/// A Verifiable Random Function (VRF) certificate containing a proof and its output.
/// </summary>
/// <param name="Proof">The VRF proof bytes.</param>
/// <param name="Output">The VRF output bytes.</param>
[CborSerializable]
[CborList]
public partial record VrfCert(
    [CborOrder(0)] byte[] Proof,
    [CborOrder(1)] byte[] Output
) : CborBase;

using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE_Sign1 message for single signer.
/// See RFC 8152 Section 4.2.
/// </summary>
[CborSerializable]
[CborList]
public partial record CoseSign1(
    [CborOrder(0)] byte[] ProtectedHeaders,
    [CborOrder(1)] HeaderMap UnprotectedHeaders,
    [CborOrder(2)][CborNullable] byte[]? Payload,
    [CborOrder(3)] byte[] Signature
) : CborBase, ICoseMessage
{
    /// <summary>
    /// Converts the COSE message to its CBOR byte representation.
    /// </summary>
    /// <returns>The CBOR-encoded bytes.</returns>
    public byte[] ToCbor()
    {
        return CborSerializer.Serialize(this);
    }

    /// <summary>
    /// Deserializes a CoseSign1 from CBOR bytes.
    /// </summary>
    /// <param name="cbor">The CBOR-encoded bytes to deserialize.</param>
    /// <returns>A deserialized CoseSign1 instance.</returns>
    public static CoseSign1 FromCbor(byte[] cbor)
    {
        ArgumentNullException.ThrowIfNull(cbor);

        return CborSerializer.Deserialize<CoseSign1>(cbor);
    }
}

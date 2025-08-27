using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE_Sign1 message for single signer
/// See RFC 8152 Section 4.2
/// </summary>
[CborSerializable]
[CborList]
public partial record CoseSign1(
    /// <summary>
    /// Protected headers as serialized byte string
    /// </summary>
    [CborOrder(0)] byte[] ProtectedHeaders,
    
    /// <summary>
    /// Unprotected headers as a map
    /// </summary>
    [CborOrder(1)] HeaderMap UnprotectedHeaders,
    
    /// <summary>
    /// Message payload (null for detached payload)
    /// </summary>
    [CborOrder(2)] [CborNullable] byte[]? Payload,
    
    /// <summary>
    /// Ed25519 signature
    /// </summary>
    [CborOrder(3)] byte[] Signature
) : CborBase, ICoseMessage
{
    /// <summary>
    /// Converts the COSE message to its CBOR byte representation
    /// </summary>
    public byte[] ToCbor() => CborSerializer.Serialize(this);
    
    /// <summary>
    /// Deserializes a CoseSign1 from CBOR bytes
    /// </summary>
    public static CoseSign1 FromCbor(byte[] cbor) => CborSerializer.Deserialize<CoseSign1>(cbor);
}
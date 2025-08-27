using Chrysalis.Cbor.Types;
using System.Formats.Cbor;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE_Sign1 message for single signer
/// See RFC 8152 Section 4.2
/// </summary>
public record CoseSign1(
    /// <summary>
    /// Protected headers as serialized byte string
    /// </summary>
    byte[] ProtectedHeaders,
    
    /// <summary>
    /// Unprotected headers as a map
    /// </summary>
    HeaderMap UnprotectedHeaders,
    
    /// <summary>
    /// Message payload (null for detached payload)
    /// </summary>
    byte[]? Payload,
    
    /// <summary>
    /// Ed25519 signature
    /// </summary>
    byte[] Signature
) : CborBase, ICoseMessage
{
    /// <summary>
    /// Converts the COSE message to its CBOR byte representation
    /// </summary>
    public byte[] ToCbor()
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        Write(writer, this);
        return writer.Encode();
    }
    
    public static void Write(CborWriter writer, CoseSign1 data)
    {
        writer.WriteStartArray(4);
        writer.WriteByteString(data.ProtectedHeaders);
        HeaderMap.Write(writer, data.UnprotectedHeaders);
        
        if (data.Payload == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteByteString(data.Payload);
        }
        
        writer.WriteByteString(data.Signature);
        writer.WriteEndArray();
    }
    
    public static new CoseSign1 Read(ReadOnlyMemory<byte> data)
    {
        var reader = new CborReader(data, CborConformanceMode.Lax);
        reader.ReadStartArray();
        
        var protectedHeaders = reader.ReadByteString();
        var unprotectedHeaders = HeaderMap.Read(reader.ReadEncodedValue());
        
        byte[]? payload = null;
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
        }
        else
        {
            payload = reader.ReadByteString();
        }
        
        var signature = reader.ReadByteString();
        
        reader.ReadEndArray();
        
        return new CoseSign1(protectedHeaders, unprotectedHeaders, payload, signature);
    }
    
    /// <summary>
    /// Deserializes a CoseSign1 from CBOR bytes
    /// </summary>
    public static CoseSign1 FromCbor(byte[] cbor) => Read(cbor);
}
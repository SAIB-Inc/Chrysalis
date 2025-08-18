using Chrysalis.Cbor.Types;
using System.Formats.Cbor;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE Sig_structure for creating signatures
/// See RFC 8152 Section 4.4
/// </summary>
public record SigStructure(
    /// <summary>
    /// Context string: "Signature", "Signature1", or "CounterSignature"
    /// </summary>
    string Context,
    
    /// <summary>
    /// Protected headers from the message body
    /// </summary>
    byte[] BodyProtected,
    
    /// <summary>
    /// Protected headers from the signer (empty for Signature1)
    /// </summary>
    byte[] SignProtected,
    
    /// <summary>
    /// External additional authenticated data
    /// </summary>
    byte[] ExternalAad,
    
    /// <summary>
    /// The payload to be signed
    /// </summary>
    byte[] Payload
) : CborBase
{
    /// <summary>
    /// Serializes the SigStructure to CBOR bytes
    /// </summary>
    public byte[] ToCbor()
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        Write(writer, this);
        return writer.Encode();
    }
    
    public static void Write(CborWriter writer, SigStructure data)
    {
        // For Signature1 context, we only write 4 elements (no SignProtected)
        // For Signature and CounterSignature, we write 5 elements
        bool includeSignProtected = data.Context != "Signature1";
        writer.WriteStartArray(includeSignProtected ? 5 : 4);
        
        writer.WriteTextString(data.Context);
        writer.WriteByteString(data.BodyProtected);
        
        // Only include SignProtected for non-Signature1 contexts
        if (includeSignProtected)
        {
            writer.WriteByteString(data.SignProtected);
        }
        
        writer.WriteByteString(data.ExternalAad);
        writer.WriteByteString(data.Payload);
        writer.WriteEndArray();
    }
    
    public static new SigStructure Read(ReadOnlyMemory<byte> data)
    {
        var reader = new CborReader(data, CborConformanceMode.Lax);
        reader.ReadStartArray();
        
        var context = reader.ReadTextString();
        var bodyProtected = reader.ReadByteString();
        var signProtected = reader.ReadByteString();
        var externalAad = reader.ReadByteString();
        var payload = reader.ReadByteString();
        
        reader.ReadEndArray();
        
        return new SigStructure(context, bodyProtected, signProtected, externalAad, payload);
    }
}
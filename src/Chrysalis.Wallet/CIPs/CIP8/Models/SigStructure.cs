using Chrysalis.Cbor.Types;
using System.Formats.Cbor;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE Sig_structure for creating signatures.
/// See RFC 8152 Section 4.4.
/// </summary>
/// <param name="Context">Context string: "Signature", "Signature1", or "CounterSignature".</param>
/// <param name="BodyProtected">Protected headers from the message body.</param>
/// <param name="SignProtected">Protected headers from the signer (empty for Signature1).</param>
/// <param name="ExternalAad">External additional authenticated data.</param>
/// <param name="Payload">The payload to be signed.</param>
public record SigStructure(
    string Context,
    byte[] BodyProtected,
    byte[] SignProtected,
    byte[] ExternalAad,
    byte[] Payload
) : CborBase
{
    /// <summary>
    /// Serializes the SigStructure to CBOR bytes.
    /// </summary>
    /// <returns>The CBOR-encoded byte representation.</returns>
    public byte[] ToCbor()
    {
        CborWriter writer = new(CborConformanceMode.Lax);
        Write(writer, this);
        return writer.Encode();
    }

    /// <summary>
    /// Writes a SigStructure to the specified CBOR writer.
    /// </summary>
    /// <param name="writer">The CBOR writer to write to.</param>
    /// <param name="data">The SigStructure data to serialize.</param>
    public static void Write(CborWriter writer, SigStructure data)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(data);

        // For Signature1 context, we only write 4 elements (no SignProtected)
        // For Signature and CounterSignature, we write 5 elements
        bool includeSignProtected = !string.Equals(data.Context, "Signature1", StringComparison.Ordinal);
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

    /// <summary>
    /// Reads a SigStructure from CBOR byte data.
    /// </summary>
    /// <param name="data">The CBOR-encoded byte data.</param>
    /// <returns>A deserialized SigStructure instance.</returns>
    public static new SigStructure Read(ReadOnlyMemory<byte> data)
    {
        CborReader reader = new(data, CborConformanceMode.Lax);
        _ = reader.ReadStartArray();

        string context = reader.ReadTextString();
        byte[] bodyProtected = reader.ReadByteString();
        byte[] signProtected = reader.ReadByteString();
        byte[] externalAad = reader.ReadByteString();
        byte[] payload = reader.ReadByteString();

        reader.ReadEndArray();

        return new SigStructure(context, bodyProtected, signProtected, externalAad, payload);
    }
}

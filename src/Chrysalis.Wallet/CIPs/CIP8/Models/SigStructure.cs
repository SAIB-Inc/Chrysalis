using System.Buffers;
using Chrysalis.Cbor.Types;
using Dahomey.Cbor.Serialization;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE Sig_structure for creating signatures.
/// See RFC 8152 Section 4.4.
/// </summary>
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
    public byte[] ToCbor()
    {
        ArrayBufferWriter<byte> output = new();
        Write(output, this);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Writes a SigStructure to the specified output buffer.
    /// </summary>
    public static void Write(IBufferWriter<byte> output, SigStructure data)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(data);

        CborWriter writer = new(output);

        // For Signature1 context, we only write 4 elements (no SignProtected)
        bool includeSignProtected = !string.Equals(data.Context, "Signature1", StringComparison.Ordinal);
        int size = includeSignProtected ? 5 : 4;
        writer.WriteBeginArray(size);

        writer.WriteString(data.Context);
        writer.WriteByteString(data.BodyProtected);

        if (includeSignProtected)
        {
            writer.WriteByteString(data.SignProtected);
        }

        writer.WriteByteString(data.ExternalAad);
        writer.WriteByteString(data.Payload);
        writer.WriteEndArray(size);
    }

    /// <summary>
    /// Reads a SigStructure from CBOR byte data.
    /// </summary>
    public static new SigStructure Read(ReadOnlyMemory<byte> data)
    {
        CborReader reader = new(data.Span);
        reader.ReadBeginArray();
        _ = reader.ReadSize();

        string context = reader.ReadString()!;
        byte[] bodyProtected = reader.ReadByteString().ToArray();
        byte[] signProtected = reader.ReadByteString().ToArray();
        byte[] externalAad = reader.ReadByteString().ToArray();
        byte[] payload = reader.ReadByteString().ToArray();

        return new SigStructure(context, bodyProtected, signProtected, externalAad, payload);
    }
}

using System.Buffers;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types;

[CborSerializable]
public partial record CborEncodedValue(ReadOnlyMemory<byte> Value) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }

    /// <summary>
    /// Wraps raw CBOR bytes in a tag-24 envelope: #6.24(bytes .cbor T).
    /// Used for Cardano inline datums and script refs which require CBOR-in-CBOR encoding.
    /// </summary>
    public static CborEncodedValue WrapTag24(byte[] cborBytes)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer);
        writer.WriteSemanticTag(24);
        writer.WriteByteString(cborBytes);
        return new CborEncodedValue(buffer.WrittenMemory);
    }
}

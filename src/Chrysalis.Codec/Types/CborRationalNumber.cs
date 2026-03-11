using System.Buffers;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types;

[CborSerializable]
[CborTag(30)]
[CborList]
[CborDefinite]
public partial record CborRationalNumber(
    [CborOrder(0)] ulong Numerator,
    [CborOrder(1)] ulong Denominator
) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }

    public static CborRationalNumber Create(ulong numerator, ulong denominator)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer);
        writer.WriteSemanticTag(30);
        writer.WriteBeginArray(2);
        writer.WriteUInt64(numerator);
        writer.WriteUInt64(denominator);
        writer.WriteEndArray(2);
        return Read((ReadOnlyMemory<byte>)buffer.WrittenMemory.ToArray());
    }
}

using System.Buffers;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Serialization;

internal static class ByteStringWire
{
    internal static ReadOnlyMemory<byte> ReadSlice(ref CborReader reader, ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> content = reader.Buffer;
        if (!content.IsEmpty && (content[0] >> 5) == 6)
        {
            CborReader probe = reader;
            try
            {
                _ = probe.TryReadSemanticTag(out _);
                content = probe.Buffer;
            }
            catch (SAIB.Cbor.CborException)
            {
                // Let the real reader report a malformed tag at its original offset.
            }
        }

        if (!content.IsEmpty && content[0] == 0x5F)
        {
            return reader.ReadByteStringToArray();
        }

        ReadOnlySpan<byte> bytes = reader.ReadByteString();
        int after = data.Length - reader.Buffer.Length;
        return data.Slice(after - bytes.Length, bytes.Length);
    }

    internal static void Read(ReadOnlyMemory<byte> data, string fieldPath, out ReadOnlyMemory<byte> value, out int bytesConsumed)
    {
        CborReader reader = new(data.Span);
        try
        {
            value = ReadSlice(ref reader, data);
        }
        catch (Exception error) when (CborError.ShouldWrapReader(error, data, reader.Buffer.Length, 0, false, fieldPath, typeof(ReadOnlyMemory<byte>)))
        {
            throw CborError.Reader(error, data, reader.Buffer.Length, 0, false, fieldPath, CborError.Expected(typeof(ReadOnlyMemory<byte>)));
        }

        bytesConsumed = data.Length - reader.Buffer.Length;
    }

    internal static void Write(IBufferWriter<byte> output, ReadOnlyMemory<byte> raw, ReadOnlyMemory<byte> value, bool tag24)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!raw.IsEmpty)
        {
            output.Write(raw.Span);
            return;
        }

        WriteFresh(output, value, tag24);
    }

    internal static ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> value, bool tag24)
    {
        ArrayBufferWriter<byte> buffer = new(value.Length + 11);
        WriteFresh(buffer, value, tag24);
        return buffer.WrittenMemory;
    }

    private static void WriteFresh(IBufferWriter<byte> output, ReadOnlyMemory<byte> value, bool tag24)
    {
        CborWriter writer = new(output);
        if (tag24)
        {
            writer.WriteSemanticTag(24);
        }
        writer.WriteByteString(value.Span);
    }
}

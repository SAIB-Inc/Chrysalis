using System.Buffers;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Benchmarks;

// Only opaque schema leaves use this visitor. Ordinary ledger fields use their SDK getters.
internal static class RawCbor
{
    internal static long ReadEveryToken(ReadOnlyMemory<byte> bytes)
    {
        CborReader reader = new(bytes.Span);
        long sum = Visit(ref reader, null);
        if (!reader.Buffer.IsEmpty) { throw new InvalidDataException("Trailing opaque CBOR bytes."); }
        return sum;
    }

    internal static ReadOnlyMemory<byte> EncodeEveryToken(ReadOnlyMemory<byte> bytes)
    {
        ArrayBufferWriter<byte> output = new();
        CborReader reader = new(bytes.Span);
        _ = Visit(ref reader, output);
        if (!reader.Buffer.IsEmpty) { throw new InvalidDataException("Trailing opaque CBOR bytes."); }
        return output.WrittenMemory;
    }

    private static long Visit(ref CborReader reader, IBufferWriter<byte>? destination)
    {
        CborWriter writer = destination is null ? default : new(destination);
        long sum = 1;
        while (reader.TryReadSemanticTag(out ulong tag))
        {
            sum = unchecked(sum + (long)tag);
            if (destination is not null) { writer.WriteSemanticTag(tag); }
        }
        CborDataItemType type = reader.GetCurrentDataItemType();
        switch (type)
        {
            case CborDataItemType.Unsigned:
                ulong unsigned = reader.ReadUInt64();
                if (destination is not null) { writer.WriteUInt64(unsigned); }
                return unchecked(sum + (long)unsigned);
            case CborDataItemType.Signed:
                long signed = reader.ReadInt64();
                if (destination is not null) { writer.WriteInt64(signed); }
                return unchecked(sum + signed);
            case CborDataItemType.Boolean:
                bool boolean = reader.ReadBoolean();
                if (destination is not null) { writer.WriteBoolean(boolean); }
                return sum + (boolean ? 1 : 0);
            case CborDataItemType.Null:
                _ = reader.ReadNull();
                if (destination is not null) { writer.WriteNull(); }
                return sum;
            case CborDataItemType.String:
                string? text = reader.ReadString();
                if (destination is not null) { writer.WriteString(text); }
                return sum + (text?.Length ?? 0);
            case CborDataItemType.ByteString:
                ReadOnlySpan<byte> bytes = reader.ReadByteString();
                if (destination is not null) { writer.WriteByteString(bytes); }
                foreach (byte value in bytes) { sum += value; }
                return sum;
            case CborDataItemType.Single:
                float single = reader.ReadSingle();
                if (destination is not null) { writer.WriteSingle(single); }
                return sum + single.GetHashCode();
            case CborDataItemType.Double:
                double number = reader.ReadDouble();
                if (destination is not null) { writer.WriteDouble(number); }
                return sum + number.GetHashCode();
            case CborDataItemType.Decimal:
                decimal decimalValue = reader.ReadDecimal();
                if (destination is not null) { writer.WriteDecimal(decimalValue); }
                return sum + decimalValue.GetHashCode();
            case CborDataItemType.Array:
            case CborDataItemType.Map:
                bool map = type == CborDataItemType.Map;
                if (map) { reader.ReadBeginMap(); } else { reader.ReadBeginArray(); }
                int size = reader.ReadSize();
                if (destination is not null)
                {
                    if (map) { writer.WriteBeginMap(size); } else { writer.WriteBeginArray(size); }
                }
                int remaining = size < 0 ? -1 : checked(size * (map ? 2 : 1));
                while (remaining > 0 || (remaining < 0 && reader.GetCurrentDataItemType() != CborDataItemType.Break))
                {
                    sum = unchecked(sum + Visit(ref reader, destination));
                    if (remaining > 0) { remaining--; }
                }
                if (size < 0) { reader.SkipDataItem(); }
                if (destination is not null)
                {
                    if (map) { writer.WriteEndMap(size); } else { writer.WriteEndArray(size); }
                }
                return sum;
            case CborDataItemType.Break:
            default:
                throw new InvalidDataException($"Unsupported opaque CBOR item: {type}");
        }
    }
}

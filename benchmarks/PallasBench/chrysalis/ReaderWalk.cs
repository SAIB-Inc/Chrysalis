using SAIB.Cbor.Serialization;

namespace Chrysalis.Benchmarks;

// No ledger objects: visit each logical item (tag/container included, break excluded).
internal static class ReaderWalk
{
    internal static long Count(ReadOnlySpan<byte> bytes)
    {
        CborReader reader = new(bytes);
        long count = Visit(ref reader);
        if (!reader.Buffer.IsEmpty) { throw new InvalidDataException("Trailing CBOR bytes."); }
        return count;
    }

    internal static void Verify()
    {
        byte[] nested = [0x82, 0xd8, 24, 0x42, 1, 2, 0xbf, 0x40, 0x83, 0xf5, 0xf6, 0x20, 0xff];
        if (Count(nested) != 9) { throw new InvalidDataException("Nested item-count check failed."); }
        if (Count([0x9f, 0xd8, 24, 0x40, 0xff]) != 3)
        {
            throw new InvalidDataException("Indefinite-container lookahead consumed a tag.");
        }
        try { _ = Count([0x01, 0x01]); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Trailing bytes were accepted.");
    }

    private static long Visit(ref CborReader reader)
    {
        long count = 1;
        while (reader.TryReadSemanticTag(out _)) { count++; }
        CborDataItemType type = reader.GetCurrentDataItemType();
        switch (type)
        {
            case CborDataItemType.Unsigned: _ = reader.ReadUInt64(); break;
            case CborDataItemType.Signed: _ = reader.ReadInt64(); break;
            case CborDataItemType.Boolean: _ = reader.ReadBoolean(); break;
            case CborDataItemType.Null: _ = reader.ReadNull(); break;
            case CborDataItemType.String: _ = reader.ReadRawString(); break;
            case CborDataItemType.ByteString: _ = reader.ReadByteString(); break;
            case CborDataItemType.Single: _ = reader.ReadSingle(); break;
            case CborDataItemType.Double: _ = reader.ReadDouble(); break;
            case CborDataItemType.Array:
            case CborDataItemType.Map:
                bool map = type == CborDataItemType.Map;
                if (map) { reader.ReadBeginMap(); } else { reader.ReadBeginArray(); }
                int size = reader.ReadSize();
                int remaining = size < 0 ? -1 : checked(size * (map ? 2 : 1));
                // Type lookahead skips tags; inspect only the raw break byte here.
                while (remaining > 0 || (remaining < 0 && !reader.Buffer.IsEmpty && reader.Buffer[0] != 0xff))
                {
                    count += Visit(ref reader);
                    if (remaining > 0) { remaining--; }
                }
                if (size < 0) { reader.SkipDataItem(); }
                break;
            case CborDataItemType.Decimal:
            case CborDataItemType.Break:
            default: throw new InvalidDataException($"Unsupported CBOR item: {type}");
        }
        return count;
    }
}

using Chrysalis.Codec.Serialization;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types.Cardano.Core.Common;

internal static class PlutusWire
{
    /// <summary>The ledger's bounded_bytes limit: at most 64 bytes in each definite byte string or chunk.</summary>
    internal const int BoundedBytesSize = 64;
    internal static IPlutusData ReadData(ReadOnlyMemory<byte> data, out int consumed) => Major(data) switch
    {
        0 or 1 => ReadInteger(data, out consumed),
        2 => PlutusBoundedBytes.Read(data, out consumed),
        4 => PlutusList.Read(data, out consumed),
        5 => PlutusMap.Read(data, out consumed),
        6 => ReadTagged(data, out consumed),
        _ => throw CborError.Decode(data, 0, nameof(IPlutusData), "Plutus data")
    };

    internal static IPlutusBigInt ReadInteger(ReadOnlyMemory<byte> data, out int consumed)
    {
        switch (Major(data))
        {
            case 0:
                return PlutusUint64.Read(data, out consumed);
            case 1:
                if (data.Length >= 9 && data.Span[0] == 0x3B && data.Span[1] >= 0x80)
                {
                    // The CBOR negative argument can occupy all 64 bits. The existing
                    // bignum value exposes its magnitude while Raw retains the original wire.
                    byte[] magnitude = new byte[9];
                    magnitude[0] = 0x48;
                    data.Span.Slice(1, 8).CopyTo(magnitude.AsSpan(1));
                    consumed = 9;
                    return new PlutusBigNint { _raw = data[..consumed], _field0 = magnitude };
                }
                return PlutusInt64.Read(data, out consumed);
            case 6:
                CborReader reader = new(data.Span);
                if (reader.TryReadSemanticTag(out ulong tag))
                {
                    switch (tag)
                    {
                        case 2: return PlutusBigUint.Read(data, out consumed);
                        case 3: return PlutusBigNint.Read(data, out consumed);
                        default: break;
                    }
                }
                break;
            default: break;
        }
        throw CborError.Decode(data, 0, nameof(IPlutusBigInt), "Plutus integer");
    }

    private static IPlutusData ReadTagged(ReadOnlyMemory<byte> data, out int consumed)
    {
        CborReader reader = new(data.Span);
        _ = reader.TryReadSemanticTag(out ulong tag);
        return tag switch
        {
            2 => PlutusBigUint.Read(data, out consumed),
            3 => PlutusBigNint.Read(data, out consumed),
            102 or (>= 121 and <= 127) or (>= 1280 and <= 1400) => ReadConstructor(data, ref reader, tag, out consumed),
            _ => throw CborError.Decode(data, 0, nameof(IPlutusData), "Plutus semantic tag", tag)
        };
    }

    internal static PlutusConstr ReadConstructor(ReadOnlyMemory<byte> data, out int consumed)
    {
        CborReader reader = new(data.Span);
        if (!reader.TryReadSemanticTag(out ulong tag) || tag is not (102 or (>= 121 and <= 127) or (>= 1280 and <= 1400)))
        {
            throw CborError.Decode(data, 0, nameof(PlutusConstr), "Plutus constructor tag");
        }
        return ReadConstructor(data, ref reader, tag, out consumed);
    }

    private static PlutusConstr ReadConstructor(ReadOnlyMemory<byte> data, ref CborReader reader, ulong tag, out int consumed)
    {
        int index = (int)tag;
        bool indefiniteEnvelope = false;
        if (tag == 102)
        {
            int arrayStart = data.Length - reader.Buffer.Length;
            reader.ReadBeginArray();
            int size = reader.ReadSize();
            if (size is not (2 or -1))
            {
                throw CborError.Decode(data, arrayStart, nameof(PlutusConstr), "two-item constructor envelope", size);
            }
            indefiniteEnvelope = size == -1;
            int offset = data.Length - reader.Buffer.Length;
            ulong alternative = reader.ReadUInt64();
            if (alternative > int.MaxValue)
            {
                throw CborError.Decode(data, offset, nameof(PlutusConstr), "constructor index out of range for Int32", alternative);
            }
            index = (int)alternative;
        }
        int start = data.Length - reader.Buffer.Length;
        if (Major(data[start..]) != 4)
        {
            throw CborError.Decode(data, start, nameof(PlutusConstr), "constructor fields array");
        }
        reader.SkipDataItem();
        int end = data.Length - reader.Buffer.Length;
        if (indefiniteEnvelope)
        {
            if (reader.Buffer.IsEmpty || reader.Buffer[0] != 0xFF)
            {
                throw CborError.Decode(data, end, nameof(PlutusConstr), "end of constructor envelope");
            }
            reader.SkipDataItem();
        }
        consumed = data.Length - reader.Buffer.Length;
        return new PlutusConstr { _raw = data[..consumed], _field0 = data[start..end], _constrIndex = index };
    }

    private static int Major(ReadOnlyMemory<byte> data) => data.IsEmpty || (data.Span[0] & 31) is 28 or 29 or 30
        ? -1
        : data.Span[0] >> 5;
}

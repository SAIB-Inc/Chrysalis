using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using SAIB.Cbor.Serialization;
using SAIB.Cbor.Util;

namespace Chrysalis.Tx.Fees;

internal static class DraftSize
{
    private static readonly ulong VKeyWidth = EncodedLength(VKeyWitness.Create(new byte[32], new byte[64]));

    [ThreadStatic]
    private static CountingBufferWriter? _sink;

    internal static ulong Width(ulong value)
    {
        CountingBufferWriter sink = _sink ??= new CountingBufferWriter();
        sink.Reset();
        CborWriter writer = new(sink);
        writer.WriteUInt64(value);
        return (ulong)sink.WrittenCount;
    }

    internal static ulong EncodedLength<T>(T value) where T : ICborType => (ulong)CborSerializer.GetEncodedLength(value);

    // Ledger size encodes [body, witnesses, auxiliary data], not the full envelope with is_valid.
    internal static ulong LedgerOverhead(PostMaryTransaction tx, ulong signingDelta, ulong? witnessLength = null) =>
        checked(1 + (witnessLength ?? EncodedLength(tx.Witnesses))
            + (tx.AuxiliaryData is { } aux ? EncodedLength(aux) : 1) + signingDelta);

    // Sign emits a definite map with canonical keys and a definite tag-258 VKey list.
    // Existing list items and other witness values keep their encoded bytes.
    internal static ulong SignedWitnessLength(ITransactionWitnessSet witnesses, int extra)
    {
        CborReader reader = new(CborSerializer.SerializeToMemory(witnesses).Span);
        reader.ReadBeginMap();
        int remaining = reader.ReadSize();
        // The signer copies the known PostAlonzo fields. Keep the original Alonzo fields too,
        // rather than copying its lossy replacement of the Alonzo witness map.
        int lastKey = witnesses is AlonzoTransactionWitnessSet ? 5 : 7;
        ulong fieldCount = 1; // The added VKey list is always present.
        ulong keyCount = (ulong)extra;
        ulong length = checked(1 + (keyCount * VKeyWidth)); // Includes map key 0.
        while (reader.MoveNextMapItem(ref remaining))
        {
            int key = reader.ReadInt32();
            bool absent = reader.Buffer[0] is 0xf6 or 0xf7;
            if (key == 0 && !absent)
            {
                _ = reader.TryReadSemanticTag(out _);
                reader.ReadBeginArray();
                int count = reader.ReadSize();
                while (count > 0 || (count < 0 && reader.Buffer[0] != 0xff))
                {
                    length = checked(length + SkipWidth(ref reader));
                    keyCount++;
                    count--;
                }
                if (count < 0)
                {
                    reader.SkipDataItem(); // Consume the old list's break without counting it.
                }
            }
            else
            {
                ulong valueWidth = SkipWidth(ref reader);
                if (!absent && key > 0 && key <= lastKey)
                {
                    fieldCount++;
                    length = checked(length + Width((ulong)key) + valueWidth);
                }
            }
        }
        // Semantic tags, array headers and map headers use the unsigned width of their argument.
        return checked(length + Width(258) + Width(keyCount) + Width(fieldCount));
    }

    private static ulong SkipWidth(ref CborReader reader)
    {
        int before = reader.Buffer.Length;
        reader.SkipDataItem();
        return (ulong)(before - reader.Buffer.Length);
    }

    internal static (ulong Fee, ulong Total) BodyWidths(ITransactionBody body, bool includeTotal)
    {
        CborReader reader = new(CborSerializer.SerializeToMemory(body).Span);
        reader.ReadBeginMap();
        int remaining = reader.ReadSize();
        ulong fee = 0;
        ulong total = 0;
        while (reader.MoveNextMapItem(ref remaining))
        {
            switch (reader.ReadUInt64())
            {
                case 2:
                    fee = ReadUIntWidth(ref reader);
                    break;
                case 17 when includeTotal:
                    total = ReadUIntWidth(ref reader);
                    break;
                default:
                    reader.SkipDataItem();
                    break;
            }
            if (fee != 0 && (!includeTotal || total != 0))
            {
                return (fee, total);
            }
        }
        throw new ArgumentException("Transaction body has no fee field or required total collateral.", nameof(body));
    }

    internal static ulong CoinWidth(IValue amount)
    {
        CborReader reader = new(CborSerializer.SerializeToMemory(amount).Span);
        if (amount is LovelaceWithMultiAsset)
        {
            reader.ReadBeginArray();
            _ = reader.ReadSize();
        }
        return ReadUIntWidth(ref reader);
    }

    internal static ITransactionOutput ChangeAt(ITransactionBody body, int index) => body.Outputs().ElementAt(index);

    private static ulong ReadUIntWidth(ref CborReader reader)
    {
        int before = reader.Buffer.Length;
        _ = reader.ReadUInt64();
        return (ulong)(before - reader.Buffer.Length);
    }
}

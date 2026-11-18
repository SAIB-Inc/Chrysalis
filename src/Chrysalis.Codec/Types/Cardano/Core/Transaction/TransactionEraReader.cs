using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types.Cardano.Core.Transaction;

internal static class TransactionEraReader
{
    internal static ITransaction Read(ReadOnlyMemory<byte> data, Era era, out int consumed)
    {
        if (era is >= Era.Shelley and <= Era.Conway)
        {
            ValidateEnvelope(data, era >= Era.Alonzo ? 4 : 3);
        }
        switch (era)
        {
            case Era.Shelley:
                ShelleyTransaction shelley = ShelleyTransaction.Read(data, out consumed);
                ValidateFields(shelley._field0, era, witnesses: false, shelley.Raw);
                ValidateFields(shelley._field1, era, witnesses: true, shelley.Raw);
                return shelley with { SelectedEra = era };
            case Era.Allegra or Era.Mary:
                AllegraTransaction allegra = AllegraTransaction.Read(data, out consumed);
                ValidateFields(allegra._field0, era, witnesses: false, allegra.Raw);
                ValidateFields(allegra._field1, era, witnesses: true, allegra.Raw);
                return allegra with { SelectedEra = era };
            case Era.Alonzo or Era.Babbage or Era.Conway:
                PostMaryTransaction modern = PostMaryTransaction.Read(data, out consumed);
                ValidateFields(modern._field0, era, witnesses: false, modern.Raw);
                ValidateFields(modern._field1, era, witnesses: true, modern.Raw);
                return modern with { SelectedEra = era };
            case Era.Byron:
            default:
                throw CborError.Decode(data, 0, nameof(ITransaction), "supported transaction era", era);
        }
    }

    internal static ITransactionBody ReadBody(ReadOnlyMemory<byte> data, out int consumed) =>
        ReadBody(data, InferBodyEra(data), out consumed);

    internal static ITransactionBody ReadBody(ReadOnlyMemory<byte> data, Era? era, out int consumed) => (era ?? InferBodyEra(data)) switch
    {
        Era.Conway => ConwayTransactionBody.Read(data, out consumed),
        Era.Babbage => BabbageTransactionBody.Read(data, out consumed),
        Era.Byron or Era.Shelley or Era.Allegra or Era.Mary or Era.Alonzo => AlonzoTransactionBody.Read(data, out consumed),
        _ => throw CborError.Decode(data, 0, nameof(ITransactionBody), "supported transaction era", era)
    };

    internal static ITransactionWitnessSet ReadWitnesses(ReadOnlyMemory<byte> data, out int consumed) =>
        PostAlonzoTransactionWitnessSet.Read(data, out consumed);

    internal static ITransactionWitnessSet ReadWitnesses(ReadOnlyMemory<byte> data, Era? era, out int consumed) =>
        era is <= Era.Alonzo ? AlonzoTransactionWitnessSet.Read(data, out consumed) : ReadWitnesses(data, out consumed);

    private static Era InferBodyEra(ReadOnlyMemory<byte> data)
    {
        // Concrete envelope reads retain their legacy era-less API. Shared keys have the
        // same meanings; newer-only keys select the corresponding superset without trials.
        CborReader reader = new(data.Span);
        reader.ReadBeginMap();
        int remaining = reader.ReadSize();
        bool hasUpdate = false;
        bool hasBabbageFields = false;
        while (HasPair(reader, remaining))
        {
            int key = reader.ReadInt32();
            if (key is >= 19 and <= 22)
            {
                return Era.Conway;
            }
            hasUpdate |= key == 6;
            hasBabbageFields |= key is >= 16 and <= 18;
            reader.SkipDataItem();
            remaining -= remaining > 0 ? 1 : 0;
        }
        // The pre-X7 concrete envelope reader returned Conway for shared-key bodies.
        // Preserve that model for compatibility; only explicit entry pins older eras.
        return hasUpdate ? (hasBabbageFields ? Era.Babbage : Era.Alonzo) : Era.Conway;
    }

    private static void ValidateEnvelope(ReadOnlyMemory<byte> data, int expected)
    {
        CborReader reader = new(data.Span);
        reader.ReadBeginArray();
        int size = reader.ReadSize();
        if (size == -1)
        {
            size = 0;
            while (size <= expected && !reader.Buffer.IsEmpty && reader.Buffer[0] != 0xFF)
            {
                reader.SkipDataItem();
                size++;
            }
        }
        if (size != expected)
        {
            throw CborError.Decode(data, 0, nameof(ITransaction), $"transaction envelope of {expected} fields", size);
        }
    }

    private static void ValidateFields(ReadOnlyMemory<byte> data, Era era, bool witnesses, ReadOnlyMemory<byte> transaction)
    {
        int origin = transaction.Length - data.Length;
        if (transaction.Span.Overlaps(data.Span, out int fieldOffset))
        {
            origin = fieldOffset;
        }
        CborReader reader = new(data.Span);
        reader.ReadBeginMap();
        int remaining = reader.ReadSize();
        uint seen = 0;
        while (HasPair(reader, remaining))
        {
            int at = origin + data.Length - reader.Buffer.Length;
            int key = reader.ReadInt32();
            bool allowed = witnesses
                ? key >= 0 && key <= (era switch { Era.Byron or Era.Shelley or Era.Allegra or Era.Mary => 2, Era.Alonzo => 5, Era.Babbage => 6, Era.Conway => 7, _ => -1 })
                : IsBodyKey(key, era);
            if (!allowed)
            {
                throw CborError.Decode(transaction, at, witnesses ? "ITransaction.Witnesses" : "ITransaction.Body", $"field key valid for {era}", key);
            }
            uint bit = 1U << key;
            if ((seen & bit) != 0)
            {
                throw CborError.Decode(transaction, at, witnesses ? "ITransaction.Witnesses" : "ITransaction.Body", "unique field key", key);
            }
            seen |= bit;
            reader.SkipDataItem();
            remaining -= remaining > 0 ? 1 : 0;
        }
    }

    private static bool IsBodyKey(int key, Era era) => key switch
    {
        (>= 0 and <= 5) or 7 => true,
        6 => era < Era.Conway,
        8 => era >= Era.Allegra,
        9 => era >= Era.Mary,
        11 or 13 or 14 or 15 => era >= Era.Alonzo,
        16 or 17 or 18 => era >= Era.Babbage,
        >= 19 and <= 22 => era >= Era.Conway,
        _ => false
    };

    private static bool HasPair(CborReader reader, int remaining) => remaining >= 0
        ? remaining > 0
        : !reader.Buffer.IsEmpty && reader.Buffer[0] != 0xFF;
}

using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Xunit;
using Spec = Utxorpc.V1Beta.Cardano;
using LedgerMetadata = Chrysalis.Codec.Types.Cardano.Core.Metadata;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Compares recursively parsed upstream metadata with ledger metadata.</summary>
internal static class ParsedMetadata
{
    internal static void Equal(IReadOnlyList<Spec.Metadata>? expected, LedgerMetadata? actual)
    {
        if (expected is null || expected.Count == 0) { Assert.Null(actual); return; }
        LedgerMetadata metadata = Assert.IsType<LedgerMetadata>(actual);
        Assert.Equal(expected.Count, metadata.Value.Count);
        foreach (Spec.Metadata item in expected)
        {
            Assert.True(Matches(item.Value, metadata.Value[item.Label]), $"Metadata label {item.Label} differs.");
        }
    }

    private static bool Matches(Spec.Metadatum expected, ITransactionMetadatum actual)
    {
        switch (expected.MetadatumCase)
        {
            case Spec.Metadatum.MetadatumOneofCase.Int:
                return actual switch
                {
                    MetadatumIntLong signed => signed.Value == expected.Int,
                    MetadatumIntUlong unsigned => expected.Int >= 0 && unsigned.Value == (ulong)expected.Int,
                    _ => false
                };
            case Spec.Metadatum.MetadatumOneofCase.Bytes:
                return actual is MetadatumBytes bytes && expected.Bytes.Span.SequenceEqual(bytes.Value);
            case Spec.Metadatum.MetadatumOneofCase.Text:
                return actual is MetadataText text && text.Value == expected.Text;
            case Spec.Metadatum.MetadatumOneofCase.Array:
                if (actual is not MetadatumList list || list.Value.Count != expected.Array.Items.Count) { return false; }
                for (int i = 0; i < list.Value.Count; i++) { if (!Matches(expected.Array.Items[i], list.Value[i])) { return false; } }
                return true;
            case Spec.Metadatum.MetadatumOneofCase.Map:
                if (actual is not MetadatumMap map || map.Value.Count != expected.Map.Pairs.Count) { return false; }
                foreach (Spec.MetadatumPair pair in expected.Map.Pairs)
                {
                    bool found = false;
                    foreach (KeyValuePair<ITransactionMetadatum, ITransactionMetadatum> entry in map.Value)
                    {
                        if (Matches(pair.Key, entry.Key) && Matches(pair.Value, entry.Value)) { found = true; break; }
                    }
                    if (!found) { return false; }
                }
                return true;
            case Spec.Metadatum.MetadatumOneofCase.None:
            default: return false;
        }
    }
}

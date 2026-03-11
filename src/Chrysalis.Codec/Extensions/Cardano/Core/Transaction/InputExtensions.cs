using CBlock = Chrysalis.Codec.Types.Cardano.Core.IBlock;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Redeemer = Chrysalis.Codec.Types.Cardano.Core.TransactionWitness.RedeemerEntry;
using Chrysalis.Codec.Extensions.Cardano.Core.TransactionWitness;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Transaction;

/// <summary>
/// Extension methods for <see cref="TransactionInput"/> to access input fields and resolve redeemers.
/// </summary>
public static class InputExtensions
{
    /// <summary>
    /// Gets the transaction ID that this input references.
    /// </summary>
    /// <param name="self">The transaction input instance.</param>
    /// <returns>The transaction ID bytes.</returns>
    public static ReadOnlyMemory<byte> TransactionId(this TransactionInput self) => self.TransactionId;

    /// <summary>
    /// Gets the output index within the referenced transaction.
    /// </summary>
    /// <param name="self">The transaction input instance.</param>
    /// <returns>The output index.</returns>
    public static ulong Index(this TransactionInput self) => self.Index;

    /// <summary>
    /// Resolves the redeemer for this input within the given block.
    /// </summary>
    /// <param name="self">The transaction input instance.</param>
    /// <param name="block">The block containing the transaction.</param>
    /// <returns>The redeemer entry, or null if not found.</returns>
    public static Redeemer? Redeemer(
        this TransactionInput self,
        CBlock block
    )
    {
        ArgumentNullException.ThrowIfNull(block);

        int txBodyIndex = block.TransactionBodies()
            .Select((tb, index) => new { tb, index })
            .Where(x => x.tb.Inputs().Any(i => i.TransactionId().Span.SequenceEqual(self.TransactionId().Span) && i.Index() == self.Index()))
            .Select(x => x.index)
            .FirstOrDefault();

        ulong inputIndex = block.TransactionBodies()
            .Select((tb, index) => new { tb, index })
            .Where(e => e.index == txBodyIndex)
            .Select(e => e.tb.Inputs()
                .OrderBy(e => string.Concat(Convert.ToHexString(e.TransactionId().Span), e.Index())))
                .Select(g => g
                    .Select((input, inputIndex) => new { input, inputIndex })
                    .Where(e => e.input.TransactionId().Span.SequenceEqual(self.TransactionId().Span))
                    .Select(e => (ulong)e.inputIndex)
                    .FirstOrDefault())
            .FirstOrDefault();

        ITransactionWitnessSet? witnessSet = block.TransactionWitnessSets()
            .Select((witnessSet, index) => new { witnessSet, index })
            .Where(e => e.index == txBodyIndex)
            .Select(e => e.witnessSet)
            .FirstOrDefault();

        if (witnessSet is null)
        {
            return null;
        }

        return witnessSet.Redeemers() switch
        {
            RedeemerList list => list.Value
                .Where(re => re.Index == inputIndex)
                .Select(re => (Redeemer?)re)
                .FirstOrDefault(),
            RedeemerMap map => map.Value
                .Where(dict => dict.Key.Index == inputIndex)
                .Select(e => (Redeemer?)CombineRedeemerEntry(e.Key, e.Value))
                .FirstOrDefault(),
            _ => null
        };
    }

    private static Redeemer CombineRedeemerEntry(RedeemerKey key, RedeemerValue value)
    {
        CborReader kr = new(key.Raw.Span);
        kr.ReadBeginArray();
        _ = kr.ReadSize();
        int keyContentStart = key.Raw.Length - kr.Buffer.Length;

        CborReader vr = new(value.Raw.Span);
        vr.ReadBeginArray();
        _ = vr.ReadSize();
        int valueContentStart = value.Raw.Length - vr.Buffer.Length;

        ReadOnlySpan<byte> keyContent = key.Raw.Span[keyContentStart..];
        ReadOnlySpan<byte> valueContent = value.Raw.Span[valueContentStart..];

        byte[] result = new byte[1 + keyContent.Length + valueContent.Length];
        result[0] = 0x84;
        keyContent.CopyTo(result.AsSpan(1));
        valueContent.CopyTo(result.AsSpan(1 + keyContent.Length));
        return RedeemerEntry.Read(result);
    }
}

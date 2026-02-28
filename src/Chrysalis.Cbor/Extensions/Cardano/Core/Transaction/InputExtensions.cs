using CBlock = Chrysalis.Cbor.Types.Cardano.Core.Block;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Redeemer = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.RedeemerEntry;
using Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

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
    public static ReadOnlyMemory<byte> TransactionId(this TransactionInput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.TransactionId;
    }

    /// <summary>
    /// Gets the output index within the referenced transaction.
    /// </summary>
    /// <param name="self">The transaction input instance.</param>
    /// <returns>The output index.</returns>
    public static ulong Index(this TransactionInput self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Index;
    }

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
        ArgumentNullException.ThrowIfNull(self);
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

        TransactionWitnessSet? witnessSet = block.TransactionWitnessSets()
            .Select((witnessSet, index) => new { witnessSet, index })
            .Where(e => e.index == txBodyIndex)
            .Select(e => e.witnessSet)
            .FirstOrDefault();

        if (witnessSet is null)
        {
            return null;
        }

        Redeemer? redeemer = witnessSet.Redeemers() switch
        {
            RedeemerList list => list.Value
                .FirstOrDefault(re => re.Index == inputIndex),
            RedeemerMap map => map.Value
                .Where(dict => dict.Key.Index == inputIndex)
                .Select(e => new Redeemer(
                    e.Key.Tag,
                    e.Key.Index,
                    e.Value.Data,
                    e.Value.ExUnits
                ))
                .FirstOrDefault(),
            _ => null
        };

        return redeemer;
    }
}

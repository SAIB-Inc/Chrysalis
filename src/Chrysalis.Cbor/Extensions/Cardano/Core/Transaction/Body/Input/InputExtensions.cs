using CBlock = Chrysalis.Cbor.Types.Cardano.Core.Block;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Redeemer = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.RedeemerEntry;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.WitnessSet;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.Input;

public static class InputExtensions
{
    public static byte[] TransactionId(this TransactionInput self) => self.TransactionId;

    public static ulong Index(this TransactionInput self) => self.Index;

    public static Redeemer? Redeemer(
        this TransactionInput self,
        CBlock block
    )
    {
        int txBodyIndex = block.TransactionBodies()
            .Select((tb, index) => new { tb, index })
            .Where(x => x.tb.Inputs().Any(i => i.TransactionId() == self.TransactionId() && i.Index() == self.Index()))
            .Select(x => x.index)
            .FirstOrDefault();

        ulong inputIndex = block.TransactionBodies()
            .Select((tb, index) => new { tb, index })
            .Where(e => e.index == txBodyIndex)
            .Select(e => e.tb.Inputs()
                .OrderBy(e => (Convert.ToHexString(e.TransactionId()) + e.Index()).ToLowerInvariant()))
                .Select(g => g
                    .Select((input, inputIndex) => new { input, inputIndex })
                    .Where(e => e.input.TransactionId() == self.TransactionId())
                    .Select(e => (ulong)e.inputIndex)
                    .FirstOrDefault())
            .FirstOrDefault();

        TransactionWitnessSet? witnessSet = block.TransactionWitnessSets()
            .Select((witnessSet, index) => new { witnessSet, index})
            .Where(e => e.index == txBodyIndex)
            .Select(e => e.witnessSet)
            .FirstOrDefault();

        if (witnessSet is null) return null;

        Redeemer? redeemer = witnessSet.Redeemers() switch
        {
            RedeemerList list => list.Value
                .Where(re => re.Index == inputIndex)
                .FirstOrDefault(),
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
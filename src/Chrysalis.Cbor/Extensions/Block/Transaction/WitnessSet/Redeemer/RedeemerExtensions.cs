using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.Redeemers;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.WitnessSet.Redeemer;

public static class RedeemerExtensions
{
    public static List<RedeemerEntry> ToList(this Redeemers self) =>
        self switch
        {
            RedeemerList list => list.Value,
            _ => []
        };

    public static Dictionary<RedeemerKey, RedeemerValue> ToDict(this Redeemers self) =>
        self switch
        {
            RedeemerMap map => map.Value,
            _ => []
        };
}
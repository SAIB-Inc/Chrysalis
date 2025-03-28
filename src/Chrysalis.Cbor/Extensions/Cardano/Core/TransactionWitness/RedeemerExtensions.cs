using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;

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

using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.Output;

public static class ValueExtensions
{
    public static ulong Lovelace(this Value self) =>
        self switch
        {
            Lovelace lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.LovelaceValue.Value,
            _ => default
        };

    public static Dictionary<byte[], TokenBundleOutput> MultiAsset(this Value self) =>
        self switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset.Value,
            _ => []
        };

    public static ulong? QuantityOf(this Value self, string subject)
    {
        if (self is LovelaceWithMultiAsset multiAsset)
        {
            ulong amount = multiAsset.MultiAsset.ToDict()
                .SelectMany(ma => 
                    ma.Value.ToDict()
                    .Where(tb =>
                    {
                        string policyId = Convert.ToHexString(ma.Key).ToLowerInvariant();
                        string assetName = Convert.ToHexString(tb.Key).ToLowerInvariant();
                        string _subject = policyId + assetName;

                        return _subject == subject;
                    })
                    .Select(tb => tb.Value)
                )
                .FirstOrDefault();
            
            return amount;
        }

        return null;
    }
}
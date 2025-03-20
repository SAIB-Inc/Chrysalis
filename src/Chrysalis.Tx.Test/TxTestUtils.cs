using System.Text;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Utils.Transaction;
using Chrysalis.Tx.Models.Keys;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Test;

public class TxTestUtils
{
    public static UnspentTransactionOutput CreateDummyUTxO(int index, ulong lovelaceAmount, int numAssets)
    {
        string indexStr = index.ToString() + index.ToString();
        string txIdHex = Convert.ToHexString(HashUtil.Blake2b256(Convert.FromHexString(indexStr)));

        return new UnspentTransactionOutput
        {
            TxHash = txIdHex,
            TxIndex = indexStr,
            Address = string.Empty, // TODO
            Amount = CreateDummyAssets(lovelaceAmount, numAssets),
            ScriptRef = null
        };
    }

    public static Value CreateDummyAssets(ulong lovelaceAmount, int numAssets)
    {
        Dictionary<CborBytes, TokenBundleOutput> multiAssetMap = [];
        for (int i = 0; i < numAssets; i++)
        {
            CborBytes assetId = new(HashUtil.Blake2b256(Encoding.UTF8.GetBytes(i.ToString())));
            ulong amount = (ulong)i;
            multiAssetMap[assetId] = new TokenBundleOutput(new Dictionary<CborBytes, CborUlong>
            {
                { assetId, new CborUlong(amount) }
            });
        }

        return new LovelaceWithMultiAsset(new Lovelace(lovelaceAmount), new MultiAssetOutput(multiAssetMap));
    }
}
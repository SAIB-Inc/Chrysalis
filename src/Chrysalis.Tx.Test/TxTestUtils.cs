using System.Text;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Utils.Transaction;
using Chrysalis.Tx.Derivations.Extensions;
using Chrysalis.Tx.Models.Keys;

namespace Chrysalis.Tx.Test;

public class TxTestUtils
{
    public static UnspentTransactionOutput CreateDummyUTxO(int index, ulong lovelaceAmount, int numAssets)
    {
        string indexStr = index.ToString();
        string txIdHex = ComputeSha256Hex(Encoding.UTF8.GetBytes(indexStr));

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
            CborBytes assetId = new(Encoding.UTF8.GetBytes(ComputeSha256Hex(Encoding.UTF8.GetBytes(i.ToString()))));
            ulong amount = (ulong)i;
            multiAssetMap[assetId] = new TokenBundleOutput(new Dictionary<CborBytes, CborUlong>
            {
                { assetId, new CborUlong(amount) }
            });
        }

        return new LovelaceWithMultiAsset(new Lovelace(lovelaceAmount), new MultiAssetOutput(multiAssetMap));
    }

    private static string ComputeSha256Hex(byte[] self)
    {
        StringBuilder stringBuilder = new();
        StringBuilder sb = stringBuilder;
        foreach (byte b in self)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    public static (PrivateKey, PublicKey) GetKeyPairFromPath(string path, PrivateKey rootKey)
    {
        PrivateKey privateKey = rootKey.Derive(path);
        return (privateKey, privateKey.GetPublicKey(false));
    }
}

using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Tx.Utils;

public static class ScriptDataHashUtil
{
    // Code inspired from CardanoSharp-Wallet: 
    // https://github.com/SAIB-Inc/cardanosharp-wallet/blob/6b35a43e97f10d33c31f690786120b567f382a40/CardanoSharp.Wallet/Utilities/ScriptUtility.cs#L12
    public static byte[] CalculateScriptDataHash(
        Redeemers redeemers,
        PlutusList datums,
        byte[] languageViews
    )
    {
        byte[] encodedBytes;

        /**
        ; script data format:
        ; [ redeemers | datums | language views ]
        ; The redeemers are exactly the data present in the transaction witness set.
        ; Similarly for the datums, if present. If no datums are provided, the middle
        ; field is an empty string.
        **/

        byte[] plutusDataBytes = [];
        if (datums != null && datums.PlutusData.Count > 0)
        {
            plutusDataBytes = datums.ToBytes() ?? [];
        }

        byte[] redeemerBytes = CborSerializer.Serialize(redeemers) ?? [];
        
        if(redeemerBytes.Length <= 0)
        {
            /**
            ; Finally, note that in the case that a transaction includes datums but does not
            ; include any redeemers, the script data format becomes (in hex):
            ; [ A0 | datums | A0 ]
            ; corresponding to a CBOR empty map and an empty map for language view.
            **/
            byte[] emptyMapBytes = Convert.FromHexString("A0");

            redeemerBytes = emptyMapBytes;
            languageViews = emptyMapBytes;
        }

        encodedBytes = new byte[redeemerBytes.Length + plutusDataBytes.Length + languageViews.Length];
        Buffer.BlockCopy(redeemerBytes, 0, encodedBytes, 0, redeemerBytes.Length);
        Buffer.BlockCopy(plutusDataBytes, 0, encodedBytes, redeemerBytes.Length, plutusDataBytes.Length);
        Buffer.BlockCopy(languageViews, 0, encodedBytes, redeemerBytes.Length + plutusDataBytes.Length, languageViews.Length);
        return HashUtil.Blake2b256(encodedBytes);
    }
}
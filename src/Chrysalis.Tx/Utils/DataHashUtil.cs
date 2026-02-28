using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Utility for computing script data hashes used in Plutus script validation.
/// </summary>
public static class DataHashUtil
{
    /// <summary>
    /// Calculates the script data hash from redeemers, datums, and language views.
    /// </summary>
    /// <param name="redeemers">The transaction redeemers.</param>
    /// <param name="datums">Optional Plutus data list.</param>
    /// <param name="languageViews">The CBOR-encoded cost model language views.</param>
    /// <returns>The Blake2b-256 hash of the script data.</returns>
    public static byte[] CalculateScriptDataHash(
        Redeemers redeemers,
        PlutusList? datums,
        byte[] languageViews
    )
    {
        ArgumentNullException.ThrowIfNull(redeemers);
        ArgumentNullException.ThrowIfNull(languageViews);

        byte[] encodedBytes;

        // script data format:
        // [ redeemers | datums | language views ]
        // The redeemers are exactly the data present in the transaction witness set.
        // Similarly for the datums, if present. If no datums are provided, the middle
        // field is an empty string.

        byte[] plutusDataBytes = [];
        if (datums != null && datums.PlutusData.GetValue().Any())
        {
            plutusDataBytes = CborSerializer.Serialize<PlutusData>(datums);
        }

        byte[] redeemerBytes = CborSerializer.Serialize(redeemers) ?? [];

        if (redeemerBytes.Length <= 0)
        {
            // Finally, note that in the case that a transaction includes datums but does not
            // include any redeemers, the script data format becomes (in hex):
            // [ A0 | datums | A0 ]
            // corresponding to a CBOR empty map and an empty map for language view.
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

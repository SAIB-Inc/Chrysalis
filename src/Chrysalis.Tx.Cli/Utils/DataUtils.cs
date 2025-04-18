using Chrysalis.Cbor.Serialization;
using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Tx.Cli.Utils;

public static class DataUtils
{
    public static string GenerateUniqueTokenName(Outref outRef, byte[] prefix, ulong index)
    {
        byte[] assetName = [];

        byte[] convertedIndex = ByteArrayUtils.FromIntBigEndian((int)index, 1);
        assetName = [.. prefix, .. convertedIndex];

        byte[] hashedOutRef = HashUtil.Blake2b256(CborSerializer.Serialize(outRef));
        assetName = [.. assetName, .. hashedOutRef];

        string assetNameCborHex = Convert.ToHexString([.. assetName.Take(32)]);
        return assetNameCborHex;
    }
}
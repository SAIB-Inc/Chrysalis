namespace Chrysalis.Tx.Cli.Templates.Models.Common;

public enum NftPrefixType
{
    ReferenceNft,
    UserNft
}

public static class NftPrefix
{
    public static byte[] GetValue(this NftPrefixType self) =>
        self switch
        {
            NftPrefixType.ReferenceNft => Convert.FromHexString("000643b0"),
            NftPrefixType.UserNft => Convert.FromHexString("000de140"),
            _ => []
        };
}
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Extensions;

public static class AddressHeaderExtensions
{
    public static string GetPrefix(this AddressHeader addressHeader)
    {
        string prefixHead = addressHeader.Type switch
        {
            AddressType.StakeKey or AddressType.ScriptStakeKey => "stake",
            _ => "addr"
        };

        string prefixTail = addressHeader.Network switch
        {
            NetworkType.Testnet or NetworkType.Preview or NetworkType.Preprod => "_test",
            NetworkType.Mainnet => string.Empty,
            _ => throw new Exception("Unknown network type")
        };
        return prefixHead + prefixTail;
    }
}
using Chrysalis.Tx.Models.Addresses;
using Chrysalis.Tx.Models.Enums;
using Chrysalis.Tx.Models.Keys;
using Chrysalis.Tx.Models.Network;

namespace Chrysalis.Tx.Utils;

 public static class AddressUtil
 {
    public static byte GetHeader(NetworkInfo networkInfo, AddressType addressType) =>
        addressType switch
        {
            AddressType.BasePayment => (byte)(networkInfo.NetworkId & 0xF),
            AddressType.ScriptPayment => (byte)(0b0001_0000 | networkInfo.NetworkId & 0xF),
            AddressType.BaseWithScriptDelegation => (byte)(0b0010_0000 | networkInfo.NetworkId & 0xF),
            AddressType.ScriptWithScriptDelegation => (byte)(0b0011_0000 | networkInfo.NetworkId & 0xF),
            AddressType.BaseWithPointerDelegation => (byte)(0b0100_0000 | networkInfo.NetworkId & 0xF),
            AddressType.ScriptWithPointerDelegation => (byte)(0b0101_0000 | networkInfo.NetworkId & 0xF),
            AddressType.EnterprisePayment => (byte)(0b0110_0000 | networkInfo.NetworkId & 0xF),
            AddressType.EnterpriseScriptPayment => (byte)(0b0111_0000 | networkInfo.NetworkId & 0xF),
            AddressType.StakeKey => (byte)(0b1110_0000 | networkInfo.NetworkId & 0xF),
            AddressType.ScriptStakeKey => (byte)(0b1111_0000 | networkInfo.NetworkId & 0xF),
            _ => throw new Exception("Unknown address type")
        };

    public static NetworkInfo GetNetworkInfo(NetworkType type) =>
        type switch
        {
            NetworkType.Testnet => new NetworkInfo(0b0000, 1097911063),
            NetworkType.Preview => new NetworkInfo(0b0000, 2),
            NetworkType.Preprod => new NetworkInfo(0b0000, 1),
            NetworkType.Mainnet => new NetworkInfo(0b0001, 764824073),
            _ => throw new Exception("Unknown network type")
        };

    public static string GetPrefix(AddressType addressType, NetworkType networkType) => $"{GetPrefixHeader(addressType)}{GetPrefixTail(networkType)}";
    
    public static string GetPrefixHeader(AddressType addressType) =>
        addressType switch
        {
            AddressType.StakeKey => "stake",
            AddressType.ScriptStakeKey => "stake",
            _ => "addr"
        };

    public static string GetPrefixTail(NetworkType networkType) =>
        networkType switch
        {
            NetworkType.Testnet => "_test",
            NetworkType.Preview => "_test",
            NetworkType.Preprod => "_test",
            NetworkType.Mainnet => "",
            _ => throw new Exception("Unknown network type")
        };

    // public static Address GetBaseAddress(PublicKey payment, PublicKey stake, NetworkType networkType)
    // {
    //     byte[] paymentEncoded = HashUtil.Blake2b224(payment.Key);
    //     byte[] stakeEncoded = HashUtil.Blake2b224(stake.Key);
    //     return GetBaseAddress(paymentEncoded, stakeEncoded, networkType);
    // }

    // public static Address GetBaseAddress(byte[] paymentEncoded, byte[] stakeEncoded, NetworkType networkType)
    // {
    //     AddressType addressType = AddressType.BasePayment;

    //     //get prefix
    //     string prefix = GetPrefix(addressType, NetworkType.Testnet);

    //     //get body
    //     byte[] addressBody = new byte[1 + paymentEncoded.Length + stakeEncoded.Length];
    //     addressBody[0] = GetHeader(GetNetworkInfo(networkType), AddressType.BasePayment);
    //     Buffer.BlockCopy(paymentEncoded, 0, addressBody, 1, paymentEncoded.Length);
    //     Buffer.BlockCopy(stakeEncoded, 0, addressBody, 1 + paymentEncoded.Length, stakeEncoded.Length);

    //     return new Address(prefix, addressBody);
    // }
 }
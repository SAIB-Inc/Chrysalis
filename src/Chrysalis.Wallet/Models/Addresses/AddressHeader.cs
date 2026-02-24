using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Models.Addresses;

public record AddressHeader(AddressType Type, NetworkType Network)
{
    public byte ToByte()
    {
        int networkNibble = Network == NetworkType.Mainnet ? 1 : 0;
        return (byte)(((byte)Type << 4) | networkNibble);
    }

    public static AddressHeader FromByte(byte headerByte)
    {
        AddressType type = (AddressType)(headerByte >> 4);
        NetworkType network = (NetworkType)(headerByte & 0x0F);
        return new AddressHeader(type, network);
    }

    public string GetPrefix()
    {
        string prefixCore = Type is AddressType.Delegation or AddressType.ScriptDelegation ? "stake" : "addr";
        string networkSuffix = Network == NetworkType.Mainnet ? string.Empty : "_test";
        return prefixCore + networkSuffix;
    }
}
using Chrysalis.Wallet.Extensions;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Addresses;

public record AddressHeader(AddressType Type, NetworkType Network, CredentialType? StakeCredentialType = null)
{
    public byte ToByte()
    {
        // If it's a stake address, use either 14 (KeyHash) or 15 (ScriptHash)
        byte header = (byte)(((byte)Type << 4) | (byte)Network);

        // Optionally, modify the header byte if it's a stake address
        if (StakeCredentialType != null)
        {
            // Set the top 4 bits for stake address type
            header |= (byte)((StakeCredentialType == CredentialType.KeyHash) ? 0x0E : 0x0F);
        }

        return header;
    }

    public static AddressHeader FromByte(byte headerByte)
    {
        // Extract type and network from the header byte
        AddressType type = (AddressType)(headerByte >> 4);
        NetworkType network = (NetworkType)(headerByte & 0x0F);

        // Extract CredentialType (0x0E = StakeKeyHash, 0x0F = ScriptHash)
        CredentialType? stakeCredentialType = null;
        if (headerByte == 0x0E)
        {
            stakeCredentialType = CredentialType.KeyHash;
        }
        else if (headerByte == 0x0F)
        {
            stakeCredentialType = CredentialType.ScriptHash;
        }

        return new AddressHeader(type, network, stakeCredentialType);
    }

    public string GetPrefix()
    {
        string prefixCore = Type.IsRewardAddress() ? "stake" : "addr";
        string networkSuffix = Network == NetworkType.Mainnet ? string.Empty : "_test";
        return prefixCore + networkSuffix;
    }
}
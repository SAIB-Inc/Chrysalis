using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Enums;
using Chrysalis.Tx.Models.Network;
using Chrysalis.Tx.Services.Encoding;

namespace Chrysalis.Tx.Models.Addresses;

public class Address
{
    private readonly byte[] _addressBytes;
    public AddressHeader AddressHeader { get; }
    public Credential PaymentCredential { get; set; }
    public Credential? StakeCredential { get; }

    public Address(byte[] addressBytes)
    {
        _addressBytes = addressBytes;
        AddressHeader = GetAddressHeader(addressBytes[0]);
        (PaymentCredential, StakeCredential) = ExtractCredentialsFromBytes(addressBytes);
    }

    public Address(string encodedAddress)
    {
        (string prefix, byte[] bytes) = Bech32Codec.Decode(encodedAddress);
        _addressBytes = bytes;
        AddressHeader = GetAddressHeader(bytes[0]);
        ValidatePrefix(prefix, AddressHeader.Network);
        (PaymentCredential, StakeCredential) = ExtractCredentialsFromBytes(bytes);
    }

    public Address(AddressType type, NetworkType network, Credential paymentPart, Credential? delegationPart)
    {
        AddressHeader = new(type, network);
        PaymentCredential = paymentPart;
        StakeCredential = delegationPart;
        _addressBytes = ConstructAddressBytes(type, network, paymentPart, delegationPart);
    }

    public Address(AddressType type, NetworkType network, byte[] paymentPart, byte[] delegationPart)
    {
        AddressHeader = new(type, network);
        CredentialType paymentType = (CredentialType)(paymentPart[0] >> 7);
        byte[] paymentHash = [.. paymentPart.Skip(1).Take(27)];
        PaymentCredential = new Credential(paymentType, paymentHash);
        
        if (delegationPart != null)
        {
            CredentialType delegationType = (CredentialType)(delegationPart[0] >> 7);
            byte[] delegationHash = [.. delegationPart.Skip(1).Take(27)];
            StakeCredential = new Credential(delegationType, delegationHash);
        }
        _addressBytes = ConstructAddressBytes(type, network, PaymentCredential, StakeCredential);

    }

    public static Address FromBytes(byte[] bytes)
    {
        return new Address(bytes);
    }

    public static Address FromBech32(string bech32String)
    {
        return new Address(bech32String);
    }

    public byte[] ToBytes() => _addressBytes;
    public string ToBech32() => Bech32Codec.Encode(_addressBytes, AddressHeader.GetPrefix());

    private static void ValidatePrefix(string prefix, NetworkType network)
    {
        string basePrefix = network switch
        {
            NetworkType.Mainnet => "addr",
            NetworkType.Testnet => "addr_test",
            NetworkType.Preview => "addr_test",
            NetworkType.Preprod => "addr_test",
            _ => throw new ArgumentOutOfRangeException(nameof(network))
        };

        string expectedPrefix = prefix.StartsWith("stake")
            ? basePrefix.Replace("addr", "stake")
            : basePrefix;

        if (prefix != expectedPrefix)
        {
            throw new ArgumentException($"Prefix '{prefix}' is invalid for network '{network}'. Expected prefix is '{expectedPrefix}'.");
        }
    }


    // TODO: Handle other AddressTypes properly
    private static byte[] ConstructAddressBytes(AddressType type, NetworkType network, Credential payment, Credential? stake)
    {
        byte header = (byte)(((byte)type << 4) | (network == NetworkType.Testnet ? 0x0F : 0x00));

        byte[] addressBytes = [header];

        switch (type)
        {
            case AddressType.BasePayment:
            case AddressType.BaseWithScriptDelegation:
            case AddressType.ScriptPayment:
                if (stake == null)
                    throw new ArgumentNullException(nameof(stake), "Stake credential cannot be null for Base addresses");

                return addressBytes.ConcatFast(payment.Hash).ConcatFast(stake.Hash);

            case AddressType.EnterprisePayment:
            case AddressType.EnterpriseScriptPayment:
            case AddressType.StakeKey:
            case AddressType.ScriptStakeKey:
                return addressBytes.ConcatFast(payment.Hash);

            default:
                throw new NotSupportedException($"Address type {type} is not supported");
        }
    }

    private static (Credential payment, Credential? stake) ExtractCredentialsFromBytes(byte[] bytes)
    {
        AddressHeader header = GetAddressHeader(bytes[0]);

        switch (header.Type)
        {
            case AddressType.BasePayment:
                if (bytes.Length < 57)
                    throw new ArgumentException("Base address must be at least 57 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    ExtractCredential(bytes, 29)
                );

            case AddressType.ScriptPayment:
                if (bytes.Length < 57)
                    throw new ArgumentException("Script payment address must be at least 57 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    ExtractCredential(bytes, 29)
                );

            case AddressType.BaseWithScriptDelegation:
                if (bytes.Length < 57)
                    throw new ArgumentException("Base with script delegation address must be at least 57 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    ExtractCredential(bytes, 29)
                );

            case AddressType.ScriptWithScriptDelegation:
                if (bytes.Length < 57)
                    throw new ArgumentException("Script with script delegation address must be at least 57 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    ExtractCredential(bytes, 29)
                );

            case AddressType.BaseWithPointerDelegation:
                if (bytes.Length < 57)
                    throw new ArgumentException("Base with pointer delegation address must be at least 57 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    ExtractCredential(bytes, 29)
                );

            case AddressType.ScriptWithPointerDelegation:
                if (bytes.Length < 57)
                    throw new ArgumentException("Script with pointer delegation address must be at least 57 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    ExtractCredential(bytes, 29)
                );

            case AddressType.EnterprisePayment:
                if (bytes.Length < 29)
                    throw new ArgumentException("Enterprise address must be at least 29 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    null
                );

            case AddressType.EnterpriseScriptPayment:
                if (bytes.Length < 29)
                    throw new ArgumentException("Enterprise script payment address must be at least 29 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    null
                );

            case AddressType.StakeKey:
                if (bytes.Length < 29)
                    throw new ArgumentException("Reward address must be at least 29 bytes");
                Credential rewardCred = ExtractCredential(bytes, 1);
                return (rewardCred, rewardCred);

            case AddressType.ScriptStakeKey:
                if (bytes.Length < 29)
                    throw new ArgumentException("Script stake key address must be at least 29 bytes");
                Credential scriptStakeCred = ExtractCredential(bytes, 1);
                return (scriptStakeCred, scriptStakeCred);

            default:
                throw new NotSupportedException($"Address type {header.Type} is not supported");
        }
    }

    private static Credential ExtractCredential(byte[] bytes, int offset)
    {
        if (bytes.Length < offset + 28)
            throw new ArgumentException("Not enough bytes to extract credential");

        CredentialType credType = (CredentialType)(bytes[offset] >> 7);
        byte[] hash = [.. bytes.Skip(offset + 1).Take(27)];

        return new Credential(credType, hash);
    }

    private static AddressHeader GetAddressHeader(byte headerByte)
    {
        int typeValue = headerByte >> 4;
        AddressType type = (AddressType)typeValue;

        NetworkType network = (headerByte & 0x0F) switch
        {
            0x0 => NetworkType.Testnet,
            0x1 => NetworkType.Mainnet,
            _ => throw new ArgumentOutOfRangeException("Network type not supported in header")
        };

        return new AddressHeader(type, network);
    }

    public static byte GetHeader(NetworkInfo networkInfo, AddressType addressType)
    {
        return addressType switch
        {
            AddressType.BasePayment => (byte)(0b0000_0000 | (networkInfo.NetworkId & 0x0F)),
            AddressType.ScriptPayment => (byte)(0b0001_0000 | (networkInfo.NetworkId & 0x0F)),
            AddressType.BaseWithScriptDelegation => (byte)(0b0010_0000 | (networkInfo.NetworkId & 0x0F)),
            AddressType.ScriptWithScriptDelegation => (byte)(0b0011_0000 | (networkInfo.NetworkId & 0x0F)),
            AddressType.BaseWithPointerDelegation => (byte)(0b0100_0000 | (networkInfo.NetworkId & 0x0F)),
            AddressType.ScriptWithPointerDelegation => (byte)(0b0101_0000 | (networkInfo.NetworkId & 0x0F)),
            AddressType.EnterprisePayment => (byte)(0b0110_0000 | (networkInfo.NetworkId & 0x0F)),
            AddressType.EnterpriseScriptPayment => (byte)(0b0111_0000 | (networkInfo.NetworkId & 0x0F)),
            _ => throw new Exception("Unknown address type")
        };
    }

}
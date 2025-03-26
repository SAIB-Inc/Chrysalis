using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Enums;
using Chrysalis.Tx.Models.Network;
using Chrysalis.Tx.Services.Encoding;

namespace Chrysalis.Tx.Models.Addresses;

public class Address
{
    private readonly byte[] _addressBytes;
    public Credential PaymentCredential { get; init; }
    public Credential? StakeCredential { get; init; }
    public AddressType Type { get; init; }
    NetworkType Network { get; init; }

    public Address(byte[] addressBytes)
    {
        _addressBytes = addressBytes;
        AddressHeader addressHeader = GetAddressHeader(addressBytes[0]);
        Type = addressHeader.Type;
        Network = addressHeader.Network;
        (PaymentCredential, StakeCredential) = ExtractCredentialsFromBytes(addressBytes);
    }

    public Address(string bech32Address)
    {
        (_, byte[] addressBytes) = Bech32Codec.Decode(bech32Address);
        AddressHeader addressHeader = GetAddressHeader(addressBytes[0]);
        Type = addressHeader.Type;
        Network = addressHeader.Network;
        _addressBytes = addressBytes;
        (PaymentCredential, StakeCredential) = ExtractCredentialsFromBytes(addressBytes);
    }

    public Address(NetworkType networkType, AddressType addressType, Credential payment, Credential? stake)
    {
        Type = addressType;
        Network = networkType;
        PaymentCredential = payment;
        StakeCredential = stake;
        _addressBytes = ConstructAddressBytes(new(addressType, networkType), payment, stake);
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
    public string ToBech32()
    {
        AddressHeader addressHeader = new(Type, Network);
        return Bech32Codec.Encode(_addressBytes, addressHeader.GetPrefix());
    }
    public string ToHex() => Convert.ToHexStringLower(_addressBytes);

    // TODO: Handle other AddressTypes properly
    private static byte[] ConstructAddressBytes(AddressHeader header, Credential payment, Credential? stake)
    {
        byte headerByte = header.ToByte();

        byte[] addressBytes = [headerByte];

        switch (header.Type)
        {
            // Base addresses: header + payment credential + stake credential
            case AddressType.BasePayment:
            case AddressType.ScriptPayment:
            case AddressType.BaseWithScriptDelegation:
            case AddressType.ScriptWithScriptDelegation:
                if (stake == null)
                    throw new ArgumentNullException(nameof(stake), "Stake credential cannot be null for Base addresses");

                // Add payment credential
                addressBytes = addressBytes.ConcatFast(payment.Hash.Value);

                // Add stake credential
                return addressBytes.ConcatFast(stake.Hash.Value);

            // TODO:
            // Pointer addresses: header + payment credential + pointer
            case AddressType.BaseWithPointerDelegation:
            case AddressType.ScriptWithPointerDelegation:
                // Add payment credential
                addressBytes = addressBytes.ConcatFast(payment.Hash.Value);

                // TODO: Add proper pointer implementation
                // For now, just return with payment credential
                return addressBytes;

            // Enterprise addresses: header + payment credential
            case AddressType.EnterprisePayment:
            case AddressType.EnterpriseScriptPayment:
                return addressBytes.ConcatFast(payment.Hash.Value);

            // Stake addresses: header + stake credential
            case AddressType.StakeKey:
            case AddressType.ScriptStakeKey:
                return addressBytes.ConcatFast(stake?.Hash.Value ?? []);

            default:
                throw new NotSupportedException($"Address type {header.Type} is not supported");
        }
    }

    public static (Credential payment, Credential? stake) ExtractCredentialsFromBytes(byte[] bytes)
    {
        AddressHeader header = GetAddressHeader(bytes[0]);

        switch (header.Type)
        {
            // Base address (type 0): payment credential + stake credential
            case AddressType.BasePayment:
            case AddressType.ScriptPayment:
            case AddressType.BaseWithScriptDelegation:
            case AddressType.ScriptWithScriptDelegation:
                if (bytes.Length < 57) // 1 byte header + 28 bytes payment credential + 28 bytes stake credential
                    throw new ArgumentException($"{header.Type} address must be at least 57 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    ExtractCredential(bytes, 29)
                );

            // TODO: Implement Pointer properly base on CIP19
            case AddressType.BaseWithPointerDelegation:
            case AddressType.ScriptWithPointerDelegation:
                if (bytes.Length < 29) // 1 byte header + 28 bytes payment credential
                    throw new ArgumentException($"{header.Type} address must be at least 29 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    null // This should actually be a pointer, not null
                );

            case AddressType.EnterprisePayment:
            case AddressType.EnterpriseScriptPayment:
                if (bytes.Length < 29)
                    throw new ArgumentException($"{header.Type} address must be at least 29 bytes");
                return (
                    ExtractCredential(bytes, 1),
                    null
                );

            case AddressType.StakeKey:
            case AddressType.ScriptStakeKey:
                if (bytes.Length < 29)
                    throw new ArgumentException($"{header.Type} address must be at least 29 bytes");
                Credential stakeCred = ExtractCredential(bytes, 1);
                return (stakeCred, stakeCred);

            default:
                throw new NotSupportedException($"Address type {header.Type} is not supported");
        }
    }

    private static Credential ExtractCredential(byte[] bytes, int offset)
    {
        if (bytes.Length < offset + 28) // Ensure we have enough bytes (1 for type + 27 for hash)
            throw new ArgumentException("Not enough bytes to extract credential");

        // The credential type is in the top bit of the first byte
        CredentialType credType = (CredentialType)(bytes[offset] >> 7);

        // Extract the full 28 bytes (including the type byte)
        byte[] hash = [.. bytes.Skip(offset).Take(28)];

        return new Credential(new((int)credType), new(hash));
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

    private static void ValidateAddressTypeWithCredentials(AddressType addressType, Credential payment, Credential? stake)
    {
        // Base addresses require both payment and stake credentials
        if ((addressType == AddressType.BasePayment ||
             addressType == AddressType.ScriptPayment ||
             addressType == AddressType.BaseWithScriptDelegation ||
             addressType == AddressType.ScriptWithScriptDelegation) && stake == null)
        {
            throw new ArgumentException($"Stake credential cannot be null for {addressType} address type");
        }

        // Enterprise addresses should not have stake credentials
        if ((addressType == AddressType.EnterprisePayment ||
             addressType == AddressType.EnterpriseScriptPayment) && stake != null)
        {
            throw new ArgumentException($"Stake credential must be null for {addressType} address type");
        }

        // Reward addresses use the payment credential as the stake credential
        if ((addressType == AddressType.StakeKey ||
             addressType == AddressType.ScriptStakeKey) && stake != null && !stake.Equals(payment))
        {
            throw new ArgumentException($"For {addressType} address type, stake credential must match payment credential or be null");
        }

        // Verify credential types match address type for payment credentials
        if ((addressType == AddressType.BasePayment ||
             addressType == AddressType.EnterprisePayment ||
             addressType == AddressType.BaseWithPointerDelegation ||
             addressType == AddressType.BaseWithScriptDelegation) &&
            (CredentialType)payment.CredentialType.Value != CredentialType.KeyHash)
        {
            throw new ArgumentException($"Payment credential must be KeyHash type for {addressType} address");
        }

        if ((addressType == AddressType.ScriptPayment ||
             addressType == AddressType.EnterpriseScriptPayment ||
             addressType == AddressType.ScriptWithPointerDelegation ||
             addressType == AddressType.ScriptWithScriptDelegation) &&
            (CredentialType)payment.CredentialType.Value != CredentialType.ScriptHash)
        {
            throw new ArgumentException($"Payment credential must be ScriptHash type for {addressType} address");
        }

        // Verify credential types match address type for stake credentials
        if (stake != null)
        {
            if ((addressType == AddressType.BasePayment ||
                 addressType == AddressType.ScriptPayment ||
                 addressType == AddressType.BaseWithPointerDelegation) &&
                (CredentialType)stake.CredentialType.Value != CredentialType.KeyHash)
            {
                throw new ArgumentException($"Stake credential must be KeyHash type for {addressType} address");
            }

            if ((addressType == AddressType.BaseWithScriptDelegation ||
                 addressType == AddressType.ScriptWithScriptDelegation ||
                 addressType == AddressType.ScriptWithPointerDelegation) &&
                (CredentialType)stake.CredentialType.Value != CredentialType.ScriptHash)
            {
                throw new ArgumentException($"Stake credential must be ScriptHash type for {addressType} address");
            }
        }
    }

    // TODO: Implement your own
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
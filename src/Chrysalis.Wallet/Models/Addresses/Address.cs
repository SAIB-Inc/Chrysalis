using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Wallet.Extensions;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.Models.Addresses;

public class Address
{
    #region Fields and Properties

    private readonly byte[] _addressBytes;
    public Credential PaymentCredential { get; set; }
    public Credential? StakeCredential { get; }
    public AddressType Type { get; init; }
    NetworkType Network { get; init; }

    #endregion

    #region Constructors

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
        (_, byte[] addressBytes) = Bech32Util.Decode(bech32Address);
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

    #endregion

    #region Public Static Factory Methods

    public static Address FromBytes(byte[] addressBytes)
    {
        return new Address(addressBytes);
    }

    public static Address FromBech32(string bech32String)
    {
        return new Address(bech32String);
    }

    public static Address FromCredentials(NetworkType networkType, AddressType addressType, byte[] paymentBytes, byte[]? stakeBytes)
    {
        Credential paymentCredential = ExtractCredential(paymentBytes, 1);
        Credential? stakeCredential = null;
        if (stakeBytes is not null)
            stakeCredential = ExtractCredential(stakeBytes, 1);

        return new Address(networkType, addressType, paymentCredential, stakeCredential);
    }

    public static Address FromPublicKey(NetworkType networkType, AddressType addressType, PublicKey paymentPub, PublicKey stakePub)
    {
        byte[] addressBody = [.. HashUtil.Blake2b224(paymentPub.Key), .. HashUtil.Blake2b224(stakePub.Key)];
        AddressHeader header = new(addressType, networkType);

        return new Address([header.ToByte(), .. addressBody]);
    }

    #endregion

    #region Public Instance Methods

    public byte[] ToBytes() => _addressBytes;
    public string ToBech32()
    {
        AddressHeader addressHeader = new(Type, Network);
        return Bech32Util.Encode(_addressBytes, addressHeader.GetPrefix());
    }

    public string ToHex() => Convert.ToHexStringLower(_addressBytes);

    public string GetPrefix() => GetAddressHeader(_addressBytes[0]).GetPrefix();

    public byte[]? GetPkh()
    {
        AddressHeader header = GetAddressHeader(_addressBytes[0]);
        int offset = 1;

        return header.Type switch
        {
            AddressType.StakeKey or
            AddressType.ScriptStakeKey => null,
            _ => _addressBytes[offset..(offset + 28)],
        };
    }

    public byte[]? GetSkh()
    {
        AddressHeader header = GetAddressHeader(_addressBytes[0]);
        int offset = 1;

        return header.Type switch
        {
            // StakeKeyHash directly follows PaymentKeyHash (offset + 28)
            AddressType.BasePayment => _addressBytes.Length >= offset + 56
                ? _addressBytes[(offset + 28)..(offset + 56)]
                : null,

            // StakeKeyHash directly follows ScriptHash (offset + 28)
            AddressType.BaseWithScriptDelegation => _addressBytes.Length >= offset + 56
                ? _addressBytes[(offset + 28)..(offset + 56)]
                : null,

            // PaymentKeyHash + ScriptHash (delegation part)
            AddressType.BaseWithPointerDelegation => null, // No StakeKeyHash, only pointer reference

            // Enterprise addresses have no delegation part
            AddressType.EnterprisePayment => null,

            // Stake-only addresses
            AddressType.StakeKey => _addressBytes.Length >= offset + 28
                ? _addressBytes[offset..(offset + 28)]
                : null,

            _ => null
        };
    }

    public static AddressHeader GetAddressHeader(byte headerByte)
    {
        int typeValue = (headerByte & 0xF0) >> 4;
        AddressType type = typeValue switch
        {
            0x00 => AddressType.BasePayment,
            0x01 => AddressType.ScriptPayment,
            0x02 => AddressType.BaseWithScriptDelegation,
            0x03 => AddressType.ScriptWithScriptDelegation,
            0x04 => AddressType.BaseWithPointerDelegation,
            0x05 => AddressType.BaseWithPointerDelegation,
            0x06 => AddressType.EnterprisePayment,
            0x07 => AddressType.EnterpriseScriptPayment,
            0x0e => AddressType.StakeKey,
            0x0f => AddressType.ScriptStakeKey,
            _ => AddressType.BasePayment
        };

        NetworkType network = (headerByte & 0x0F) switch
        {
            0x0 => NetworkType.Testnet,
            0x1 => NetworkType.Mainnet,
            _ => throw new ArgumentOutOfRangeException("Network type not supported in header")
        };

        return new AddressHeader(type, network);
    }

    #endregion

    #region Private Helper Methods

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
                addressBytes = addressBytes.ConcatFast(payment.Raw!.Value.ToArray());
                return addressBytes.ConcatFast(stake.Raw!.Value.ToArray());

            // TODO:
            // Pointer addresses: header + payment credential + pointer
            case AddressType.BaseWithPointerDelegation:
            case AddressType.ScriptWithPointerDelegation:
                // Add payment credential
                addressBytes = addressBytes.ConcatFast(payment.Raw!.Value.ToArray());

                // TODO: Add proper pointer implementation
                // For now, just return with payment credential
                return addressBytes;

            // Enterprise addresses: header + payment credential
            case AddressType.EnterprisePayment:
            case AddressType.EnterpriseScriptPayment:
                return addressBytes.ConcatFast(payment.Raw!.Value.ToArray());

            // Stake addresses: header + stake credential
            case AddressType.StakeKey:
            case AddressType.ScriptStakeKey:
                return addressBytes.ConcatFast(stake?.Raw!.Value.ToArray() ?? []);

            default:
                throw new NotSupportedException($"Address type {header.Type} is not supported");
        }
    }

    private static (Credential payment, Credential? stake) ExtractCredentialsFromBytes(byte[] addressBytes)
    {
        AddressHeader header = GetAddressHeader(addressBytes[0]);

        void ValidateAddressLength(int requiredLength)
        {
            if (addressBytes.Length < requiredLength)
                throw new ArgumentException($"Address must be at least {requiredLength} bytes");
        }

        (Credential payment, Credential? stake) ExtractPaymentAndStakeCredentials()
        {
            return (
                ExtractCredential(addressBytes, 1),
                ExtractCredential(addressBytes, 29)
            );
        }

        switch (header.Type)
        {
            case AddressType.BasePayment:
            case AddressType.ScriptPayment:
            case AddressType.BaseWithScriptDelegation:
            case AddressType.ScriptWithScriptDelegation:
            case AddressType.BaseWithPointerDelegation:
            case AddressType.ScriptWithPointerDelegation:
                ValidateAddressLength(57); // Base and script address types require at least 57 bytes
                return ExtractPaymentAndStakeCredentials();

            case AddressType.EnterprisePayment:
            case AddressType.EnterpriseScriptPayment:
                ValidateAddressLength(29); // Enterprise address types require at least 29 bytes
                return (ExtractCredential(addressBytes, 1), null);

            case AddressType.StakeKey:
            case AddressType.ScriptStakeKey:
                ValidateAddressLength(29); // Reward address types require at least 29 bytes
                Credential rewardCred = ExtractCredential(addressBytes, 1);
                return (rewardCred, rewardCred);

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

        Credential credential = credType switch
        {
            CredentialType.KeyHash => new VerificationKey(hash),
            CredentialType.ScriptHash => new Script(hash),
            _ => throw new ArgumentException("Unsupported credential type")
        };

        return credential;
    }

    #endregion
}
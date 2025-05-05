using Chrysalis.Cbor.Serialization;
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

    /// <summary>
    /// Creates an <see cref="Address"/> instance directly from raw address bytes.
    /// </summary>
    /// <param name="addressBytes">The raw byte representation of the address.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    public static Address FromBytes(byte[] addressBytes)
    {
        return new Address(addressBytes);
    }

    /// <summary>
    /// Creates an <see cref="Address"/> instance from a Bech32-encoded address string.
    /// </summary>
    /// <param name="bech32String">The Bech32-encoded Cardano address string.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    public static Address FromBech32(string bech32String) => new(bech32String);

    /// <summary>
    /// Creates an <see cref="Address"/> instance from payment and optional stake credentials.
    /// </summary>
    /// <param name="networkType">The Cardano network type (e.g., Mainnet or Testnet).</param>
    /// <param name="addressType">The type of address being created.</param>
    /// <param name="paymentBytes">The payment credential bytes (e.g., PaymentKeyHash).</param>
    /// <param name="stakeBytes">Optional stake credential bytes (e.g., StakeKeyHash).</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    public static Address FromCredentials(NetworkType networkType, AddressType addressType, byte[] paymentBytes, byte[]? stakeBytes)
    {
        Credential paymentCredential = ExtractCredential(paymentBytes, 1);
        Credential? stakeCredential = null;
        if (stakeBytes is not null) stakeCredential = ExtractCredential(stakeBytes, 1);

        return new Address(networkType, addressType, paymentCredential, stakeCredential);
    }

    /// <summary>
    /// Creates an <see cref="Address"/> from public key(s).
    /// </summary>
    /// <param name="networkType">The Cardano network type (e.g., Mainnet or Testnet).</param>
    /// <param name="addressType">The type of address to create.</param>
    /// <param name="paymentPub">The public key for the payment part.</param>
    /// <param name="stakePub">Optional public key for the stake delegation part.</param>
    public static Address FromPublicKeys(NetworkType networkType, AddressType addressType, PublicKey paymentPub, PublicKey? stakePub = null)
    {
        byte[] paymentHash = HashUtil.Blake2b224(paymentPub.Key);
        byte[]? stakeHash = stakePub?.Key is not null ? HashUtil.Blake2b224(stakePub.Key) : null;

        return FromCredentials(networkType, addressType, paymentHash, stakeHash);
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

    public byte[]? GetPaymentKeyHash() =>
        Type is AddressType.StakeKey or AddressType.ScriptStakeKey
            ? null
            : _addressBytes.Length >= 29 ? _addressBytes[1..29] : null;

    public byte[]? GetStakeKeyHash() => Type switch
    {
        // Payment (28 bytes) + Stake (28 bytes)
        AddressType.BasePayment
        or AddressType.ScriptPayment
        or AddressType.BaseWithScriptDelegation
        or AddressType.ScriptWithScriptDelegation
            => _addressBytes.Length >= 57 ? _addressBytes[29..57] : null,

        // Stake-only addresses (stake hash at offset 1)
        AddressType.StakeKey
        or AddressType.ScriptStakeKey
            => _addressBytes.Length >= 29 ? _addressBytes[1..29] : null,

        _ => null
    };

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
                addressBytes = addressBytes.ConcatFast(payment.Raw.HasValue ? payment.Raw!.Value.ToArray() : GetKeyHash(payment));
                return addressBytes.ConcatFast(stake.Raw.HasValue ? stake.Raw.Value.ToArray() : GetKeyHash(stake));

            // TODO:
            // Pointer addresses: header + payment credential + pointer
            case AddressType.BaseWithPointerDelegation:
            case AddressType.ScriptWithPointerDelegation:
                // Add payment credential
                addressBytes = addressBytes.ConcatFast(payment.Raw.HasValue ? payment.Raw.Value.ToArray() : GetKeyHash(payment));

                // TODO: Add proper pointer implementation
                // For now, just return with payment credential
                return addressBytes;

            // Enterprise addresses: header + payment credential
            case AddressType.EnterprisePayment:
            case AddressType.EnterpriseScriptPayment:
                return addressBytes.ConcatFast(payment.Raw.HasValue ? payment.Raw.Value.ToArray() : GetKeyHash(payment));

            // Stake addresses: header + stake credential
            case AddressType.StakeKey:
            case AddressType.ScriptStakeKey:
                return addressBytes.ConcatFast(stake!.Raw.HasValue ? stake.Raw.Value.ToArray() : GetKeyHash(stake));

            default:
                throw new NotSupportedException($"Address type {header.Type} is not supported");
        }
    }


    // TODO
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
                ExtractCredential(addressBytes, 0),
                ExtractCredential(addressBytes, 28)
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
        if (bytes.Length < 28)
            throw new ArgumentException("Not enough bytes to extract credential");

        CredentialType credType = (CredentialType)(bytes[offset] >> 7);
        byte[] hash = [.. bytes.Skip(offset).Take(29)];

        Credential credential = credType switch
        {
            CredentialType.KeyHash => new VerificationKey(hash),
            CredentialType.ScriptHash => new Script(hash),
            _ => throw new ArgumentException("Unsupported credential type")
        };

        return credential;
    }

    private static byte[] GetKeyHash(Credential self) => self switch
    {
        VerificationKey vkey => vkey.VerificationKeyHash,
        Script script => script.ScriptHash,
        _ => throw new Exception("Invalid credential")
    };

    #endregion
}
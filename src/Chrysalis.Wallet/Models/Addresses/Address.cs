using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Wallet.Extensions;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.Models.Addresses;

/// <summary>
/// Represents a Cardano address with support for various address types and encoding formats.
/// </summary>
public class Address
{
    #region Fields and Properties

    private readonly byte[] _addressBytes;

    /// <summary>
    /// Gets the address type.
    /// </summary>
    public AddressType Type { get; init; }

    /// <summary>
    /// Gets the network type.
    /// </summary>
    public NetworkType Network { get; init; }

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an Address from raw address bytes.
    /// </summary>
    /// <param name="addressBytes">The raw byte representation of the address.</param>
    public Address(byte[] addressBytes)
    {
        ArgumentNullException.ThrowIfNull(addressBytes);

        _addressBytes = addressBytes;
        AddressHeader addressHeader = GetAddressHeader(addressBytes[0]);
        Type = addressHeader.Type;
        Network = addressHeader.Network;
    }

    /// <summary>
    /// Creates an Address from a Bech32-encoded string.
    /// </summary>
    /// <param name="bech32Address">The Bech32-encoded Cardano address string.</param>
    public Address(string bech32Address)
    {
        ArgumentNullException.ThrowIfNull(bech32Address);

        (_, byte[] addressBytes) = Bech32Util.Decode(bech32Address);
        AddressHeader addressHeader = GetAddressHeader(addressBytes[0]);
        Type = addressHeader.Type;
        Network = addressHeader.Network;
        _addressBytes = addressBytes;
    }

    /// <summary>
    /// Creates an Address from network type, address type, and credential bytes.
    /// </summary>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="addressType">The address type.</param>
    /// <param name="payment">The payment credential bytes.</param>
    /// <param name="stake">The optional stake credential bytes.</param>
    public Address(NetworkType networkType, AddressType addressType, byte[] payment, byte[]? stake)
    {
        ArgumentNullException.ThrowIfNull(payment);

        Type = addressType;
        Network = networkType;
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
    public static Address FromBech32(string bech32String)
    {
        return new(bech32String);
    }

    /// <summary>
    /// Creates an <see cref="Address"/> instance from payment and optional stake credentials.
    /// </summary>
    /// <param name="networkType">The Cardano network type (e.g., Mainnet or Testnet).</param>
    /// <param name="addressType">The type of address being created.</param>
    /// <param name="paymentBytes">The payment credential bytes (e.g., PaymentKeyHash).</param>
    /// <param name="stakeBytes">Optional stake credential bytes (e.g., StakeKeyHash).</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    public static Address FromCredentials(NetworkType networkType, AddressType addressType, Credential paymentBytes, Credential? stakeBytes)
    {
        ArgumentNullException.ThrowIfNull(paymentBytes);

        byte[] pkh = paymentBytes switch
        {
            VerificationKey vkey => vkey.VerificationKeyHash.ToArray(),
            Script script => script.ScriptHash.ToArray(),
            _ => throw new ArgumentException("Invalid payment credential type", nameof(paymentBytes))
        };

        byte[]? skh = stakeBytes switch
        {
            VerificationKey vkey => vkey.VerificationKeyHash.ToArray(),
            Script script => script.ScriptHash.ToArray(),
            null => null,
            _ => throw new ArgumentException("Invalid stake credential type", nameof(stakeBytes))
        };

        return new Address(networkType, addressType, pkh, skh);
    }

    /// <summary>
    /// Creates an <see cref="Address"/> from public key(s) with an explicit address type.
    /// For delegation/stake types (14, 15), <paramref name="paymentPub"/> is used as the stake credential.
    /// For all other types, <paramref name="paymentPub"/> is the payment credential and
    /// <paramref name="stakePub"/> is the optional stake credential.
    /// </summary>
    /// <param name="networkType">The Cardano network type (e.g., Mainnet or Testnet).</param>
    /// <param name="addressType">The address type to create.</param>
    /// <param name="paymentPub">The public key for the payment credential (or stake credential for delegation types).</param>
    /// <param name="stakePub">Optional public key for the stake credential.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    public static Address FromPublicKeys(NetworkType networkType, AddressType addressType, PublicKey paymentPub, PublicKey? stakePub = null)
    {
        ArgumentNullException.ThrowIfNull(paymentPub);

        if (addressType is AddressType.Delegation or AddressType.ScriptDelegation)
        {
            byte[] stakeHash = HashUtil.Blake2b224(paymentPub.Key);
            return new Address(networkType, addressType, [], stakeHash);
        }

        byte[] paymentHash = HashUtil.Blake2b224(paymentPub.Key);
        byte[]? stakeHash2 = stakePub is not null ? HashUtil.Blake2b224(stakePub.Key) : null;
        return new Address(networkType, addressType, paymentHash, stakeHash2);
    }

    #endregion

    #region Public Instance Methods

    /// <summary>
    /// Returns the raw byte representation of the address.
    /// </summary>
    /// <returns>The address bytes.</returns>
    public byte[] ToBytes()
    {
        return _addressBytes;
    }

    /// <summary>
    /// Encodes the address as a Bech32 string.
    /// </summary>
    /// <returns>The Bech32-encoded address string.</returns>
    public string ToBech32()
    {
        AddressHeader addressHeader = new(Type, Network);
        return Bech32Util.Encode(_addressBytes, addressHeader.Prefix);
    }

    /// <summary>
    /// Returns the hexadecimal representation of the address bytes.
    /// </summary>
    /// <returns>The hex-encoded address string.</returns>
    public string ToHex()
    {
        return Convert.ToHexStringLower(_addressBytes);
    }

    /// <summary>
    /// Gets the Bech32 prefix for this address.
    /// </summary>
    /// <returns>The address prefix string.</returns>
    public string GetPrefix()
    {
        return GetAddressHeader(_addressBytes[0]).Prefix;
    }

    /// <summary>
    /// Extracts the payment key hash from the address bytes.
    /// </summary>
    /// <returns>The 28-byte payment key hash, or null if not applicable.</returns>
    public byte[]? GetPaymentKeyHash()
    {
        return Type is AddressType.Delegation or AddressType.ScriptDelegation
            ? null
            : _addressBytes.Length >= 29 ? _addressBytes[1..29] : null;
    }

    /// <summary>
    /// Extracts the stake key hash from the address bytes.
    /// </summary>
    /// <returns>The 28-byte stake key hash, or null if not applicable.</returns>
    public byte[]? GetStakeKeyHash()
    {
        return Type switch
        {
            // Payment (28 bytes) + Stake (28 bytes)
            AddressType.Base
            or AddressType.ScriptPaymentWithDelegation
            or AddressType.PaymentWithScriptDelegation
            or AddressType.ScriptPaymentWithScriptDelegation
                => _addressBytes.Length >= 57 ? _addressBytes[29..57] : null,

            // Stake-only addresses (stake hash at offset 1)
            AddressType.Delegation
            or AddressType.ScriptDelegation
                => _addressBytes.Length >= 29 ? _addressBytes[1..29] : null,
            // Pointer and enterprise addresses have no stake key hash
            AddressType.PaymentWithPointerDelegation
            or AddressType.ScriptPaymentWithPointerDelegation
            or AddressType.EnterprisePayment
            or AddressType.EnterpriseScriptPayment => null,
            _ => null
        };
    }

    /// <summary>
    /// Parses a header byte into an AddressHeader containing the address type and network type.
    /// </summary>
    /// <param name="headerByte">The header byte to parse.</param>
    /// <returns>An AddressHeader representing the parsed type and network.</returns>
    public static AddressHeader GetAddressHeader(byte headerByte)
    {
        int typeValue = (headerByte & 0xF0) >> 4;
        AddressType type = typeValue switch
        {
            0x00 => AddressType.Base,
            0x01 => AddressType.ScriptPaymentWithDelegation,
            0x02 => AddressType.PaymentWithScriptDelegation,
            0x03 => AddressType.ScriptPaymentWithScriptDelegation,
            0x04 => AddressType.PaymentWithPointerDelegation,
            0x05 => AddressType.ScriptPaymentWithPointerDelegation,
            0x06 => AddressType.EnterprisePayment,
            0x07 => AddressType.EnterpriseScriptPayment,
            0x0e => AddressType.Delegation,
            0x0f => AddressType.ScriptDelegation,
            _ => throw new ArgumentOutOfRangeException(nameof(headerByte), $"Unsupported address type nibble: 0x{typeValue:X2}")
        };

        NetworkType network = (headerByte & 0x0F) switch
        {
            0x0 => NetworkType.Testnet,
            0x1 => NetworkType.Mainnet,
            _ => throw new ArgumentOutOfRangeException(nameof(headerByte), "Network type not supported in header")
        };

        return new AddressHeader(type, network);
    }

    #endregion

    #region Private Helper Methods

    private static byte[] ConstructAddressBytes(AddressHeader header, byte[] payment, byte[]? stake)
    {
        byte headerByte = header.ToByte();
        byte[] addressBytes = [headerByte];

        switch (header.Type)
        {
            // Base addresses: header + payment credential + stake credential
            case AddressType.Base:
            case AddressType.ScriptPaymentWithDelegation:
            case AddressType.PaymentWithScriptDelegation:
            case AddressType.ScriptPaymentWithScriptDelegation:
                if (stake == null)
                {
                    throw new ArgumentNullException(nameof(stake), "Stake credential cannot be null for Base addresses");
                }

                addressBytes = addressBytes.ConcatFast(payment);
                return addressBytes.ConcatFast(stake);

            // TODO:
            // Pointer addresses: header + payment credential + pointer
            case AddressType.PaymentWithPointerDelegation:
            case AddressType.ScriptPaymentWithPointerDelegation:
                // Add payment credential
                addressBytes = addressBytes.ConcatFast(payment);

                // TODO: Add proper pointer implementation
                // For now, just return with payment credential
                return addressBytes;

            // Enterprise addresses: header + payment credential
            case AddressType.EnterprisePayment:
            case AddressType.EnterpriseScriptPayment:
                return addressBytes.ConcatFast(payment);

            case AddressType.Delegation:
            case AddressType.ScriptDelegation:
                return addressBytes.ConcatFast(stake!);

            default:
                throw new NotSupportedException($"Address type {header.Type} is not supported");
        }
    }

    #endregion
}

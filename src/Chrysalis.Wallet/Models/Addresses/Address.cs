using System.Runtime.CompilerServices;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Plutus.Address;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using PlutusAddress = Chrysalis.Codec.Types.Plutus.Address.Address;

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
    /// <exception cref="ArgumentException"><paramref name="addressBytes"/> is empty.</exception>
    public Address(byte[] addressBytes)
    {
        ArgumentNullException.ThrowIfNull(addressBytes);
        AddressHeader addressHeader = ValidateAddressBytes(addressBytes);
        _addressBytes = addressBytes;
        Type = addressHeader.Type;
        Network = addressHeader.Network;
    }

    /// <summary>
    /// Creates an Address from raw address bytes, copying the bytes into an owned array after validation.
    /// </summary>
    /// <param name="addressBytes">The raw byte representation of the address.</param>
    /// <exception cref="ArgumentException"><paramref name="addressBytes"/> is empty.</exception>
    public Address(ReadOnlySpan<byte> addressBytes)
    {
        AddressHeader addressHeader = ValidateAddressBytes(addressBytes);
        _addressBytes = addressBytes.ToArray();
        Type = addressHeader.Type;
        Network = addressHeader.Network;
    }

    /// <summary>
    /// Creates an Address from a Bech32-encoded string.
    /// </summary>
    /// <param name="bech32Address">The Bech32-encoded Cardano address string.</param>
    /// <exception cref="FormatException">The string holds no address bytes, or its prefix is not the one its header gives.</exception>
    public Address(string bech32Address)
    {
        ArgumentNullException.ThrowIfNull(bech32Address);

        (string prefix, byte[] addressBytes) = Bech32Util.Decode(bech32Address);
        if (addressBytes.Length == 0)
        {
            throw new FormatException("The Bech32 string holds no address bytes.");
        }

        AddressHeader addressHeader = GetAddressHeader(addressBytes[0]);
        if (prefix != addressHeader.Prefix)
        {
            throw new FormatException($"The prefix {prefix} does not match the address header, which gives {addressHeader.Prefix}.");
        }

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
    /// <exception cref="NotSupportedException">For a pointer address type: pointer addresses are read, not built.</exception>
    public Address(NetworkType networkType, AddressType addressType, byte[] payment, byte[]? stake)
    {
        ArgumentNullException.ThrowIfNull(payment);

        Type = addressType;
        Network = networkType;
        _addressBytes = ConstructAddressBytes(new(addressType, networkType), payment, stake, stake is not null);
    }

    private Address(NetworkType networkType, AddressType addressType, ReadOnlySpan<byte> payment, ReadOnlySpan<byte> stake, bool hasStake)
    {
        Type = addressType;
        Network = networkType;
        _addressBytes = ConstructAddressBytes(new(addressType, networkType), payment, stake, hasStake);
    }

    #endregion

    #region Public Static Factory Methods

    /// <summary>
    /// Creates an <see cref="Address"/> instance directly from raw address bytes.
    /// </summary>
    /// <param name="addressBytes">The raw byte representation of the address.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="addressBytes"/> is empty.</exception>
    public static Address FromBytes(byte[] addressBytes) => new(addressBytes);

    /// <summary>
    /// Creates an <see cref="Address"/> instance from raw address bytes, copying the bytes into an owned array after validation.
    /// </summary>
    /// <param name="addressBytes">The raw byte representation of the address.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="addressBytes"/> is empty.</exception>
    public static Address FromBytes(ReadOnlySpan<byte> addressBytes) => new(addressBytes);

    /// <summary>
    /// Creates an <see cref="Address"/> instance from a Bech32-encoded address string.
    /// </summary>
    /// <param name="bech32String">The Bech32-encoded Cardano address string.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="FormatException">The string holds no address bytes, or its prefix is not the one its header gives.</exception>
    public static Address FromBech32(string bech32String) => new(bech32String);

    /// <summary>
    /// Creates an <see cref="Address"/> instance from payment and optional stake credentials.
    /// </summary>
    /// <param name="networkType">The Cardano network type (e.g., Mainnet or Testnet).</param>
    /// <param name="addressType">The type of address being created.</param>
    /// <param name="paymentBytes">The payment credential bytes (e.g., PaymentKeyHash).</param>
    /// <param name="stakeBytes">Optional stake credential bytes (e.g., StakeKeyHash).</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="NotSupportedException">For a pointer address type: pointer addresses are read, not built.</exception>
    public static Address FromCredentials(NetworkType networkType, AddressType addressType, ICredential paymentBytes, ICredential? stakeBytes)
    {
        ArgumentNullException.ThrowIfNull(paymentBytes);

        ReadOnlySpan<byte> pkh = paymentBytes switch
        {
            VerificationKey vkey => vkey.VerificationKeyHash.Span,
            Script script => script.ScriptHash.Span,
            _ => throw new ArgumentException("Invalid payment credential type", nameof(paymentBytes))
        };

        ReadOnlySpan<byte> skh = stakeBytes switch
        {
            VerificationKey vkey => vkey.VerificationKeyHash.Span,
            Script script => script.ScriptHash.Span,
            null => [],
            _ => throw new ArgumentException("Invalid stake credential type", nameof(stakeBytes))
        };

        return new Address(networkType, addressType, pkh, skh, stakeBytes is not null);
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
    /// <exception cref="NotSupportedException">For a pointer address type: pointer addresses are read, not built.</exception>
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

    /// <summary>
    /// Creates an enterprise script address from a script hash.
    /// </summary>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="scriptHash">The 28-byte script hash.</param>
    /// <returns>An enterprise script payment address.</returns>
    public static Address FromScriptHash(NetworkType networkType, byte[] scriptHash) =>
        new(networkType, AddressType.EnterpriseScriptPayment, scriptHash, null);

    /// <summary>
    /// Creates an enterprise script address from a script hash.
    /// </summary>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="scriptHash">The 28-byte script hash.</param>
    /// <returns>An enterprise script payment address.</returns>
    public static Address FromScriptHash(NetworkType networkType, ReadOnlySpan<byte> scriptHash) =>
        new(networkType, AddressType.EnterpriseScriptPayment, scriptHash, [], hasStake: false);

    /// <summary>
    /// Creates an enterprise script address from a hex-encoded script hash.
    /// </summary>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="scriptHashHex">The 56-character hex-encoded script hash.</param>
    /// <returns>An enterprise script payment address.</returns>
    public static Address FromScriptHash(NetworkType networkType, string scriptHashHex) =>
        FromScriptHash(networkType, Convert.FromHexString(scriptHashHex));

    /// <summary>
    /// Creates an <see cref="Address"/> from a hex-encoded address string.
    /// </summary>
    /// <param name="hex">The hex-encoded address bytes.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="ArgumentException">The hex string holds no bytes (parameter name: <c>addressBytes</c>).</exception>
    public static Address FromHex(string hex) => new(Convert.FromHexString(hex));

    /// <summary>
    /// Creates a base script address (script payment + stake delegation) from a script hash and stake key hash.
    /// </summary>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="scriptHash">The 28-byte script hash.</param>
    /// <param name="stakeKeyHash">The 28-byte stake key hash.</param>
    /// <returns>A script payment address with stake delegation.</returns>
    public static Address FromScriptHashWithStake(NetworkType networkType, byte[] scriptHash, byte[] stakeKeyHash) =>
        new(networkType, AddressType.ScriptPaymentWithDelegation, scriptHash, stakeKeyHash);

    /// <summary>
    /// Creates a base script address (script payment + stake delegation) from a script hash and stake key hash.
    /// </summary>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="scriptHash">The 28-byte script hash.</param>
    /// <param name="stakeKeyHash">The 28-byte stake key hash.</param>
    /// <returns>A script payment address with stake delegation.</returns>
    [OverloadResolutionPriority(-1)]
    public static Address FromScriptHashWithStake(NetworkType networkType, ReadOnlySpan<byte> scriptHash, ReadOnlySpan<byte> stakeKeyHash) =>
        new(networkType, AddressType.ScriptPaymentWithDelegation, scriptHash, stakeKeyHash, hasStake: true);

    #endregion

    #region Public Instance Methods

    /// <summary>
    /// Returns the raw byte representation of the address.
    /// </summary>
    /// <returns>The address bytes.</returns>
    public byte[] ToBytes() => _addressBytes;

    /// <summary>
    /// Encodes the address as a Bech32 string.
    /// </summary>
    /// <returns>The Bech32-encoded address string.</returns>
    public string ToBech32() => Bech32Util.Encode(_addressBytes, AddressHeader.GetPrefix(Type, Network));

    /// <summary>
    /// Returns the hexadecimal representation of the address bytes.
    /// </summary>
    /// <returns>The hex-encoded address string.</returns>
    public string ToHex() => Convert.ToHexStringLower(_addressBytes);

    /// <summary>
    /// Gets the Bech32 prefix for this address.
    /// </summary>
    /// <returns>The address prefix string.</returns>
    public string GetPrefix() => GetAddressHeader(_addressBytes[0]).Prefix;

    /// <summary>
    /// Extracts the payment key hash from the address bytes.
    /// </summary>
    /// <returns>The 28-byte payment key hash, or null if not applicable.</returns>
    public byte[]? GetPaymentKeyHash() => Type is AddressType.Delegation or AddressType.ScriptDelegation
            ? null
            : _addressBytes.Length >= 29 ? _addressBytes[1..29] : null;

    /// <summary>
    /// Extracts the payment key hash as a lowercase hex string.
    /// </summary>
    /// <returns>The 56-character hex payment key hash, or null if not applicable.</returns>
    public string? GetPaymentKeyHashHex()
    {
        byte[]? hash = GetPaymentKeyHash();
        return hash is not null ? Convert.ToHexStringLower(hash) : null;
    }

    /// <summary>
    /// Extracts the stake key hash as a lowercase hex string.
    /// </summary>
    /// <returns>The 56-character hex stake key hash, or null if not applicable.</returns>
    public string? GetStakeKeyHashHex()
    {
        byte[]? hash = GetStakeKeyHash();
        return hash is not null ? Convert.ToHexStringLower(hash) : null;
    }

    /// <summary>
    /// Extracts the stake key hash from the address bytes.
    /// </summary>
    /// <returns>The 28-byte stake key hash, or null if not applicable.</returns>
    public byte[]? GetStakeKeyHash() => Type switch
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

    /// <summary>
    /// Converts this address to a Plutus script context address.
    /// Maps payment and stake credentials to their Plutus equivalents.
    /// </summary>
    /// <returns>A Plutus Address with the correct credential types.</returns>
    /// <exception cref="NotSupportedException">For a pointer address.</exception>
    public PlutusAddress ToPlutusAddress()
    {
        ICredential paymentCred = Type switch
        {
            AddressType.Base
            or AddressType.EnterprisePayment
            or AddressType.PaymentWithScriptDelegation
            or AddressType.Delegation
                => VerificationKey.Create(GetPaymentKeyHash()!),
            AddressType.ScriptPaymentWithDelegation
            or AddressType.EnterpriseScriptPayment
            or AddressType.ScriptPaymentWithScriptDelegation
            or AddressType.ScriptDelegation
                => Script.Create(GetPaymentKeyHash()!),
            AddressType.PaymentWithPointerDelegation or AddressType.ScriptPaymentWithPointerDelegation
                => throw new NotSupportedException("Converting a pointer address to a Plutus address is not supported."),
            _ => throw new InvalidOperationException($"Unsupported address type: {Type}")
        };

        byte[]? stakeHash = GetStakeKeyHash();
        if (stakeHash is not null)
        {
            ICredential stakeCred = Type is AddressType.PaymentWithScriptDelegation
                or AddressType.ScriptPaymentWithScriptDelegation
                or AddressType.ScriptDelegation
                ? Script.Create(stakeHash)
                : VerificationKey.Create(stakeHash);
            return new PlutusAddress(
                paymentCred,
                Some<Inline<ICredential>>.Create(Inline<ICredential>.Create(stakeCred)));
        }

        return new PlutusAddress(paymentCred, new None<Inline<ICredential>>());
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

    private static AddressHeader ValidateAddressBytes(ReadOnlySpan<byte> addressBytes) =>
        addressBytes.IsEmpty
            ? throw new ArgumentException("An address has at least its header byte.", nameof(addressBytes))
            : GetAddressHeader(addressBytes[0]);

    private static byte[] ConstructAddressBytes(AddressHeader header, ReadOnlySpan<byte> payment, ReadOnlySpan<byte> stake, bool hasStake)
    {
        byte headerByte = header.ToByte();

        switch (header.Type)
        {
            // Base addresses: header + payment credential + stake credential
            case AddressType.Base:
            case AddressType.ScriptPaymentWithDelegation:
            case AddressType.PaymentWithScriptDelegation:
            case AddressType.ScriptPaymentWithScriptDelegation:
                if (!hasStake)
                {
                    throw new ArgumentNullException(nameof(stake), "Stake credential cannot be null for Base addresses");
                }

                byte[] baseBytes = new byte[1 + payment.Length + stake.Length];
                baseBytes[0] = headerByte;
                payment.CopyTo(baseBytes.AsSpan(1));
                stake.CopyTo(baseBytes.AsSpan(1 + payment.Length));
                return baseBytes;

            // Enterprise addresses: header + payment credential
            case AddressType.EnterprisePayment:
            case AddressType.EnterpriseScriptPayment:
                byte[] enterpriseBytes = new byte[1 + payment.Length];
                enterpriseBytes[0] = headerByte;
                payment.CopyTo(enterpriseBytes.AsSpan(1));
                return enterpriseBytes;

            case AddressType.Delegation:
            case AddressType.ScriptDelegation:
                if (!hasStake)
                {
                    throw new ArgumentNullException(nameof(stake), "Stake credential cannot be null for Delegation addresses");
                }

                byte[] delegationBytes = new byte[1 + stake.Length];
                delegationBytes[0] = headerByte;
                stake.CopyTo(delegationBytes.AsSpan(1));
                return delegationBytes;

            case AddressType.PaymentWithPointerDelegation:
            case AddressType.ScriptPaymentWithPointerDelegation:
            default:
                throw new NotSupportedException($"Address type {header.Type} is not supported");
        }
    }

    #endregion
}

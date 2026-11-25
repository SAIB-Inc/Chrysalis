using System.Runtime.CompilerServices;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Plutus.Address;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using SimpleBase;
using PlutusAddress = Chrysalis.Codec.Types.Plutus.Address.Address;

namespace Chrysalis.Wallet.Models.Addresses;

/// <summary>
/// Represents a Cardano address with support for Shelley, pointer, stake, and read-only Byron address formats.
/// </summary>
public class Address
{
    internal const int CredentialHashLength = 28;
    internal const byte ByronEnvelopeHeader = 0x82;

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

    /// <summary>Gets the Cardano era of this address.</summary>
    public AddressEra Era => Type == AddressType.Byron ? AddressEra.Byron : AddressEra.Shelley;

    /// <summary>Gets the decoded Byron address metadata, or <see langword="null"/> for Shelley-era addresses.</summary>
    public ByronAddressInfo? Byron { get; }

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an Address from raw address bytes, validating CIP-0019 lengths or Byron CBOR/CRC-32 structure.
    /// Byron addresses copy defensively on ingress; Shelley addresses retain legacy caller-array sharing.
    /// </summary>
    /// <param name="addressBytes">The raw byte representation of the address.</param>
    /// <exception cref="ArgumentNullException"><paramref name="addressBytes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="addressBytes"/> is empty, fails CIP-0019 validation, or is malformed Byron CBOR.</exception>
    public Address(byte[] addressBytes)
    {
        ArgumentNullException.ThrowIfNull(addressBytes);
        if (addressBytes.Length > 0 && addressBytes[0] == ByronEnvelopeHeader)
        {
            Byron = DecodeByronAndCopy(addressBytes, out _addressBytes);
            Type = AddressType.Byron;
            Network = GetByronNetwork(Byron);
            return;
        }

        AddressHeader addressHeader = ValidateAndParseShelleyBytes(addressBytes);
        _addressBytes = addressBytes;
        Type = addressHeader.Type;
        Network = addressHeader.Network;
    }

    /// <summary>
    /// Creates an Address from raw address bytes, copying the bytes into an owned array after validation.
    /// </summary>
    /// <param name="addressBytes">The raw byte representation of the address.</param>
    /// <exception cref="ArgumentException"><paramref name="addressBytes"/> is empty, fails CIP-0019 validation, or is malformed Byron CBOR.</exception>
    public Address(ReadOnlySpan<byte> addressBytes)
    {
        if (!addressBytes.IsEmpty && addressBytes[0] == ByronEnvelopeHeader)
        {
            Byron = DecodeByronAndCopy(addressBytes, out _addressBytes);
            Type = AddressType.Byron;
            Network = GetByronNetwork(Byron);
            return;
        }

        AddressHeader addressHeader = ValidateAndParseShelleyBytes(addressBytes);
        _addressBytes = addressBytes.ToArray();
        Type = addressHeader.Type;
        Network = addressHeader.Network;
    }

    /// <summary>
    /// Creates an Address from a Bech32-encoded string.
    /// </summary>
    /// <param name="bech32Address">The Bech32-encoded Cardano address string.</param>
    /// <exception cref="FormatException">The string is invalid Bech32, holds no bytes, encodes Byron, mismatches prefix, or fails CIP-0019 validation.</exception>
    public Address(string bech32Address)
    {
        ArgumentNullException.ThrowIfNull(bech32Address);

        (string prefix, byte[] addressBytes) = Bech32Util.Decode(bech32Address);
        if (addressBytes.Length == 0)
        {
            throw new FormatException("The Bech32 string holds no address bytes.");
        }

        if (addressBytes[0] == ByronEnvelopeHeader)
        {
            throw new FormatException("Byron addresses must use Base58 encoding, not Bech32.");
        }

        AddressHeader addressHeader = GetAddressHeader(addressBytes[0]);
        if (prefix != addressHeader.Prefix)
        {
            throw new FormatException($"The prefix {prefix} does not match the address header, which gives {addressHeader.Prefix}.");
        }

        if (!ValidateShelleyPayload(addressHeader.Type, addressBytes, out string? error))
        {
            throw new FormatException(error);
        }

        Type = addressHeader.Type;
        Network = addressHeader.Network;
        _addressBytes = addressBytes;
    }

    /// <summary>
    /// Creates an Address from network type, address type, and credential or canonical pointer bytes.
    /// </summary>
    /// <remarks>For pointer types, this constructs wire data; from the Conway ledger era, new pointer addresses cannot be added to Mainnet.</remarks>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="addressType">The address type.</param>
    /// <param name="payment">The 28-byte payment credential bytes (ignored for delegation types).</param>
    /// <param name="stake">The optional 28-byte stake credential bytes or canonical VLQ pointer bytes.</param>
    /// <exception cref="ArgumentException">A credential hash is not 28 bytes, or pointer bytes are not canonical CIP-0019 VLQ.</exception>
    /// <exception cref="NotSupportedException">For <see cref="AddressType.Byron"/> (Byron decoding is read-only).</exception>
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

    private Address(byte[] ownedByronBytes, ByronAddressInfo byron)
    {
        _addressBytes = ownedByronBytes;
        Byron = byron;
        Type = AddressType.Byron;
        Network = GetByronNetwork(byron);
    }

    #endregion

    #region Public Static Factory Methods

    /// <summary>Creates an <see cref="Address"/> directly from raw address bytes.</summary>
    /// <param name="addressBytes">The raw byte representation of the address.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="addressBytes"/> is empty, fails CIP-0019 validation, or is malformed Byron CBOR.</exception>
    public static Address FromBytes(byte[] addressBytes) => new(addressBytes);

    /// <summary>Creates an <see cref="Address"/> from raw address bytes, copying into an owned array after validation.</summary>
    /// <param name="addressBytes">The raw byte representation of the address.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="addressBytes"/> is empty, fails CIP-0019 validation, or is malformed Byron CBOR.</exception>
    public static Address FromBytes(ReadOnlySpan<byte> addressBytes) => new(addressBytes);

    /// <summary>Creates an <see cref="Address"/> from a Bech32-encoded address string.</summary>
    /// <param name="bech32String">The Bech32-encoded Cardano address string.</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="FormatException">The string is invalid Bech32, holds no bytes, encodes Byron, mismatches prefix, or fails CIP-0019 validation.</exception>
    public static Address FromBech32(string bech32String) => new(bech32String);

    /// <summary>Creates a read-only Byron <see cref="Address"/> from a Base58-encoded Byron bootstrap address.</summary>
    /// <param name="base58Address">The Base58-encoded Byron address string.</param>
    /// <returns>An <see cref="Address"/> instance representing the decoded Byron address.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="base58Address"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="base58Address"/> is not a valid Base58-encoded Byron address.</exception>
    public static Address FromBase58(string base58Address)
    {
        ArgumentNullException.ThrowIfNull(base58Address);
        if (base58Address.Length == 0)
        {
            throw new FormatException("The Base58 string holds no address bytes.");
        }

        byte[] raw;
        try { raw = Base58.Bitcoin.Decode(base58Address); }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) { throw new FormatException("Invalid Base58 address encoding.", ex); }

        if (!ByronAddressInfo.TryDecode(raw, out ByronAddressSlice slice, out string? error))
        {
            throw new FormatException(error);
        }

        return new Address(raw, slice.Materialize(raw));
    }

    /// <summary>Creates a CIP-0019 pointer address from a 28-byte payment hash and stake-registration pointer.</summary>
    /// <remarks>This constructs wire data; from the Conway ledger era, new pointer addresses cannot be added to Mainnet.</remarks>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="paymentHash">The 28-byte payment key hash or script hash.</param>
    /// <param name="addressPointer">The stake-registration pointer.</param>
    /// <param name="isScriptPayment"><see langword="true"/> for a script payment credential; <see langword="false"/> for a key hash.</param>
    /// <returns>A pointer <see cref="Address"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="paymentHash"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="paymentHash"/> is not 28 bytes.</exception>
    public static Address FromPointer(NetworkType networkType, byte[] paymentHash, AddressPointer addressPointer, bool isScriptPayment = false)
    {
        ArgumentNullException.ThrowIfNull(paymentHash);
        return FromPointer(networkType, paymentHash.AsSpan(), addressPointer, isScriptPayment);
    }

    /// <summary>Creates a CIP-0019 pointer address from a 28-byte payment hash span and stake-registration pointer.</summary>
    /// <remarks>This constructs wire data; from the Conway ledger era, new pointer addresses cannot be added to Mainnet.</remarks>
    /// <param name="networkType">The Cardano network type.</param>
    /// <param name="paymentHash">The 28-byte payment key hash or script hash.</param>
    /// <param name="addressPointer">The stake-registration pointer.</param>
    /// <param name="isScriptPayment"><see langword="true"/> for a script payment credential; <see langword="false"/> for a key hash.</param>
    /// <returns>A pointer <see cref="Address"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="paymentHash"/> is not 28 bytes.</exception>
    public static Address FromPointer(NetworkType networkType, ReadOnlySpan<byte> paymentHash, AddressPointer addressPointer, bool isScriptPayment = false)
    {
        if (paymentHash.Length != CredentialHashLength)
        {
            throw new ArgumentException($"Payment credential must be {CredentialHashLength} bytes.", nameof(paymentHash));
        }

        AddressType addressType = isScriptPayment ? AddressType.ScriptPaymentWithPointerDelegation : AddressType.PaymentWithPointerDelegation;
        byte[] bytes = new byte[1 + CredentialHashLength + addressPointer.EncodedByteLength];
        bytes[0] = new AddressHeader(addressType, networkType).ToByte();
        paymentHash.CopyTo(bytes.AsSpan(1));
        _ = addressPointer.TryWriteBytes(bytes.AsSpan(1 + CredentialHashLength), out _);
        return new Address(bytes) { Network = networkType };
    }

    /// <summary>
    /// Creates an <see cref="Address"/> instance from payment and optional stake credentials.
    /// </summary>
    /// <param name="networkType">The Cardano network type (e.g., Mainnet or Testnet).</param>
    /// <param name="addressType">The type of address being created.</param>
    /// <param name="paymentBytes">The payment credential bytes (e.g., PaymentKeyHash).</param>
    /// <param name="stakeBytes">Optional stake credential bytes (e.g., StakeKeyHash).</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="NotSupportedException">For a pointer or Byron address type; use <see cref="FromPointer(NetworkType, byte[], AddressPointer, bool)"/> for pointers.</exception>
    public static Address FromCredentials(NetworkType networkType, AddressType addressType, ICredential paymentBytes, ICredential? stakeBytes)
    {
        ArgumentNullException.ThrowIfNull(paymentBytes);
        if (addressType is AddressType.PaymentWithPointerDelegation or AddressType.ScriptPaymentWithPointerDelegation or AddressType.Byron)
        {
            throw new NotSupportedException($"Address type {addressType} is not supported by {nameof(FromCredentials)}.");
        }

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
    /// <exception cref="NotSupportedException">For a pointer or Byron address type; use <see cref="FromPointer(NetworkType, byte[], AddressPointer, bool)"/> for pointers.</exception>
    public static Address FromPublicKeys(NetworkType networkType, AddressType addressType, PublicKey paymentPub, PublicKey? stakePub = null)
    {
        ArgumentNullException.ThrowIfNull(paymentPub);
        if (addressType is AddressType.PaymentWithPointerDelegation or AddressType.ScriptPaymentWithPointerDelegation or AddressType.Byron)
        {
            throw new NotSupportedException($"Address type {addressType} is not supported by {nameof(FromPublicKeys)}.");
        }

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
    /// <param name="hex">The hex-encoded address bytes (Shelley, pointer, or Byron).</param>
    /// <returns>An <see cref="Address"/> instance.</returns>
    /// <exception cref="ArgumentException">The hex string holds no bytes, fails CIP-0019 validation, or is malformed Byron CBOR (parameter name: <c>addressBytes</c>).</exception>
    public static Address FromHex(string hex) => FromOwnedBytes(Convert.FromHexString(hex));

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
    /// Returns the raw byte representation of the address (defensive copy for Byron; legacy shared array for Shelley).
    /// </summary>
    /// <returns>The address bytes.</returns>
    public byte[] ToBytes() => Byron is not null ? [.. _addressBytes] : _addressBytes;

    /// <summary>Encodes the address as a Bech32 string.</summary>
    /// <returns>The Bech32-encoded address string.</returns>
    /// <exception cref="NotSupportedException">This is a Byron address; use <see cref="ToBase58"/> instead.</exception>
    public string ToBech32() => Type == AddressType.Byron
        ? throw new NotSupportedException("Byron addresses use Base58 encoding, not Bech32.")
        : Bech32Util.Encode(_addressBytes, AddressHeader.GetPrefix(Type, Network));

    /// <summary>Encodes a Byron address as a Base58 string.</summary>
    /// <returns>The Base58-encoded Byron address string.</returns>
    /// <exception cref="NotSupportedException">This address is not a Byron address.</exception>
    public string ToBase58() => Type == AddressType.Byron
        ? Base58.Bitcoin.Encode(_addressBytes)
        : throw new NotSupportedException("Only Byron addresses use Base58 encoding.");

    /// <summary>
    /// Returns the hexadecimal representation of the address bytes.
    /// </summary>
    /// <returns>The hex-encoded address string.</returns>
    public string ToHex() => Convert.ToHexStringLower(_addressBytes);

    /// <summary>Gets the Bech32 prefix for this address.</summary>
    /// <returns>The address prefix string.</returns>
    /// <exception cref="NotSupportedException">This is a Byron address, which has no Bech32 prefix.</exception>
    public string GetPrefix() => Type == AddressType.Byron
        ? throw new NotSupportedException("Byron addresses use Base58 encoding and have no Bech32 prefix.")
        : GetAddressHeader(_addressBytes[0]).Prefix;

    /// <summary>
    /// Extracts the payment key hash from the address bytes.
    /// </summary>
    /// <returns>The 28-byte payment key hash, or null if not applicable.</returns>
    public byte[]? GetPaymentKeyHash() => Type is AddressType.Delegation or AddressType.ScriptDelegation or AddressType.Byron
        ? null
        : _addressBytes.Length >= 1 + CredentialHashLength ? _addressBytes[1..(1 + CredentialHashLength)] : null;

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
        // Pointer, enterprise, and Byron addresses have no stake key hash
        AddressType.PaymentWithPointerDelegation
        or AddressType.ScriptPaymentWithPointerDelegation
        or AddressType.EnterprisePayment
        or AddressType.EnterpriseScriptPayment
        or AddressType.Byron => null,
        _ => null
    };

    /// <summary>Extracts the stake-registration pointer from a pointer address.</summary>
    /// <returns>The decoded <see cref="AddressPointer"/>, or <see langword="null"/> if this is not a pointer address.</returns>
    public AddressPointer? GetPointer() => Type is AddressType.PaymentWithPointerDelegation or AddressType.ScriptPaymentWithPointerDelegation
        ? AddressPointer.FromBytes(_addressBytes.AsSpan(1 + CredentialHashLength))
        : null;

    /// <summary>
    /// Converts this address to a Plutus script context address.
    /// Maps payment and stake credentials to their Plutus equivalents.
    /// </summary>
    /// <returns>A Plutus Address with the correct credential types.</returns>
    /// <exception cref="NotSupportedException">For a pointer, stake-only, or Byron address.</exception>
    public PlutusAddress ToPlutusAddress()
    {
        ICredential paymentCred = Type switch
        {
            AddressType.Base
            or AddressType.EnterprisePayment
            or AddressType.PaymentWithScriptDelegation
                => VerificationKey.Create(_addressBytes.AsMemory(1, CredentialHashLength)),
            AddressType.ScriptPaymentWithDelegation
            or AddressType.EnterpriseScriptPayment
            or AddressType.ScriptPaymentWithScriptDelegation
                => Script.Create(_addressBytes.AsMemory(1, CredentialHashLength)),
            AddressType.PaymentWithPointerDelegation or AddressType.ScriptPaymentWithPointerDelegation
                => throw new NotSupportedException("Converting a pointer address to a Plutus address is not supported; use ToPlutusStakeReference() or GetPointer()."),
            AddressType.Delegation or AddressType.ScriptDelegation
                => throw new NotSupportedException("Converting a stake-only address to a Plutus address is not supported."),
            AddressType.Byron
                => throw new NotSupportedException("Converting a Byron address to a Plutus address is not supported."),
            _ => throw new InvalidOperationException($"Unsupported address type: {Type}")
        };

        ICredential? stakeCred = CreatePlutusStakeCredential();
        return stakeCred is not null
            ? new PlutusAddress(
                paymentCred,
                Some<Inline<ICredential>>.Create(Inline<ICredential>.Create(stakeCred)))
            : new PlutusAddress(paymentCred, new None<Inline<ICredential>>());
    }

    /// <summary>Converts this address's staking component to a Plutus <see cref="ICborOption{T}"/> of <see cref="IReferenced{T}"/>.</summary>
    /// <returns>A Plutus optional staking reference (<see cref="Inline{T}"/>, <see cref="PointerRef{T}"/>, or <see cref="None{T}"/>).</returns>
    /// <exception cref="NotSupportedException">This is a Byron address.</exception>
    public ICborOption<IReferenced<ICredential>> ToPlutusStakeReference()
    {
        if (Type == AddressType.Byron)
        {
            throw new NotSupportedException("Converting a Byron address to a Plutus stake reference is not supported.");
        }

        if (Type is AddressType.PaymentWithPointerDelegation or AddressType.ScriptPaymentWithPointerDelegation)
        {
            AddressPointer ptr = AddressPointer.FromBytes(_addressBytes.AsSpan(1 + CredentialHashLength));
            return Some<IReferenced<ICredential>>.Create(
                PointerRef<ICredential>.Create(ptr.Slot, ptr.TransactionIndex, ptr.CertificateIndex));
        }

        ICredential? stakeCred = CreatePlutusStakeCredential();
        return stakeCred is not null
            ? Some<IReferenced<ICredential>>.Create(Inline<ICredential>.Create(stakeCred))
            : None<IReferenced<ICredential>>.Create();
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

    private static NetworkType GetByronNetwork(ByronAddressInfo byron) =>
        byron.NetworkMagic is null ? NetworkType.Mainnet : NetworkType.Testnet;

    private static ByronAddressInfo DecodeByronAndCopy(ReadOnlySpan<byte> addressBytes, out byte[] owned)
    {
        if (!ByronAddressInfo.TryDecode(addressBytes, out ByronAddressSlice slice, out string? error))
        {
            throw new ArgumentException(error, nameof(addressBytes));
        }

        owned = addressBytes.ToArray();
        return slice.Materialize(owned);
    }

    private static Address FromOwnedBytes(byte[] addressBytes)
    {
        if (addressBytes.Length > 0 && addressBytes[0] == ByronEnvelopeHeader)
        {
            return ByronAddressInfo.TryDecode(addressBytes, out ByronAddressSlice slice, out string? error)
                ? new Address(addressBytes, slice.Materialize(addressBytes))
                : throw new ArgumentException(error, nameof(addressBytes));
        }

        return new Address(addressBytes);
    }

    private ICredential? CreatePlutusStakeCredential() => Type switch
    {
        AddressType.Base or AddressType.ScriptPaymentWithDelegation
            => VerificationKey.Create(_addressBytes.AsMemory(1 + CredentialHashLength, CredentialHashLength)),
        AddressType.PaymentWithScriptDelegation or AddressType.ScriptPaymentWithScriptDelegation
            => Script.Create(_addressBytes.AsMemory(1 + CredentialHashLength, CredentialHashLength)),
        AddressType.Delegation => VerificationKey.Create(_addressBytes.AsMemory(1, CredentialHashLength)),
        AddressType.ScriptDelegation => Script.Create(_addressBytes.AsMemory(1, CredentialHashLength)),
        AddressType.PaymentWithPointerDelegation
        or AddressType.ScriptPaymentWithPointerDelegation
        or AddressType.EnterprisePayment
        or AddressType.EnterpriseScriptPayment
        or AddressType.Byron => null,
        _ => null
    };

    private static AddressHeader ValidateAndParseShelleyBytes(ReadOnlySpan<byte> addressBytes)
    {
        if (addressBytes.IsEmpty)
        {
            throw new ArgumentException("An address has at least its header byte.", nameof(addressBytes));
        }

        AddressHeader header = GetAddressHeader(addressBytes[0]);
        return ValidateShelleyPayload(header.Type, addressBytes, out string? error)
            ? header
            : throw new ArgumentException(error, nameof(addressBytes));
    }

    private static bool ValidateShelleyPayload(AddressType type, ReadOnlySpan<byte> bytes, out string? error)
    {
        bool valid = type switch
        {
            AddressType.Base
            or AddressType.ScriptPaymentWithDelegation
            or AddressType.PaymentWithScriptDelegation
            or AddressType.ScriptPaymentWithScriptDelegation
                => bytes.Length == 1 + CredentialHashLength + CredentialHashLength,
            AddressType.EnterprisePayment or AddressType.EnterpriseScriptPayment => bytes.Length == 1 + CredentialHashLength,
            AddressType.Delegation or AddressType.ScriptDelegation => bytes.Length == 1 + CredentialHashLength,
            AddressType.PaymentWithPointerDelegation or AddressType.ScriptPaymentWithPointerDelegation
                => bytes.Length >= 1 + CredentialHashLength + 3 && AddressPointer.TryFromBytes(bytes[(1 + CredentialHashLength)..], out _),
            AddressType.Byron => false,
            _ => false
        };

        error = valid ? null : $"Invalid CIP-0019 payload for address type {type}.";
        return valid;
    }

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
                if (payment.Length != CredentialHashLength) { throw new ArgumentException($"Payment credential must be {CredentialHashLength} bytes.", nameof(payment)); }
                if (!hasStake) { throw new ArgumentNullException(nameof(stake), "Stake credential cannot be null for Base addresses"); }
                if (stake.Length != CredentialHashLength) { throw new ArgumentException($"Stake credential must be {CredentialHashLength} bytes.", nameof(stake)); }

                byte[] baseBytes = new byte[1 + CredentialHashLength + CredentialHashLength];
                baseBytes[0] = headerByte;
                payment.CopyTo(baseBytes.AsSpan(1));
                stake.CopyTo(baseBytes.AsSpan(1 + CredentialHashLength));
                return baseBytes;

            // Enterprise addresses: header + payment credential
            case AddressType.EnterprisePayment:
            case AddressType.EnterpriseScriptPayment:
                if (payment.Length != CredentialHashLength) { throw new ArgumentException($"Payment credential must be {CredentialHashLength} bytes.", nameof(payment)); }

                byte[] enterpriseBytes = new byte[1 + CredentialHashLength];
                enterpriseBytes[0] = headerByte;
                payment.CopyTo(enterpriseBytes.AsSpan(1));
                return enterpriseBytes;

            case AddressType.Delegation:
            case AddressType.ScriptDelegation:
                if (!hasStake) { throw new ArgumentNullException(nameof(stake), "Stake credential cannot be null for Delegation addresses"); }
                if (stake.Length != CredentialHashLength) { throw new ArgumentException($"Stake credential must be {CredentialHashLength} bytes.", nameof(stake)); }

                byte[] delegationBytes = new byte[1 + CredentialHashLength];
                delegationBytes[0] = headerByte;
                stake.CopyTo(delegationBytes.AsSpan(1));
                return delegationBytes;

            case AddressType.PaymentWithPointerDelegation:
            case AddressType.ScriptPaymentWithPointerDelegation:
                if (payment.Length != CredentialHashLength) { throw new ArgumentException($"Payment credential must be {CredentialHashLength} bytes.", nameof(payment)); }
                if (!hasStake) { throw new ArgumentNullException(nameof(stake), "Pointer bytes cannot be null for Pointer addresses"); }
                if (!AddressPointer.TryFromBytes(stake, out _)) { throw new ArgumentException("Invalid canonical pointer bytes.", nameof(stake)); }

                byte[] pointerBytes = new byte[1 + CredentialHashLength + stake.Length];
                pointerBytes[0] = headerByte;
                payment.CopyTo(pointerBytes.AsSpan(1));
                stake.CopyTo(pointerBytes.AsSpan(1 + CredentialHashLength));
                return pointerBytes;

            case AddressType.Byron:
            default:
                throw new NotSupportedException($"Address type {header.Type} is not supported");
        }
    }

    #endregion
}

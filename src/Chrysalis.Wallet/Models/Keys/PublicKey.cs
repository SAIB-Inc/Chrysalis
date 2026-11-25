using Chrysalis.Crypto;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.Models.Keys;

/// <summary>
/// Represents a Cardano Ed25519 public key with an associated chain code.
/// </summary>
/// <param name="key">The 32-byte public key bytes.</param>
/// <param name="chaincode">The 32-byte chain code for extended key derivation, or empty for non-extended keys.</param>
public class PublicKey(byte[] key, byte[] chaincode)
{
    /// <summary>
    /// Gets or sets the public key bytes.
    /// </summary>
    public byte[] Key { get; set; } = key;

    /// <summary>
    /// Gets or sets the chain code bytes.
    /// </summary>
    public byte[] Chaincode { get; set; } = chaincode;

    /// <summary>
    /// Derives a soft child public key at the specified index using BIP32-Ed25519 V2.
    /// </summary>
    /// <param name="index">The non-hardened derivation index, 0 to 2^31 - 1.</param>
    /// <returns>The derived child public key.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">This key is not a valid 32-byte canonical Ed25519 extended public key with a 32-byte chain code.</exception>
    public PublicKey Derive(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        if (Key is null || Key.Length != Ed25519.PublicKeySizeInBytes)
        {
            throw new InvalidOperationException($"A public key must be {Ed25519.PublicKeySizeInBytes} bytes to derive child keys, not {Key?.Length ?? 0}.");
        }

        if (Chaincode is null || Chaincode.Length != Bip32Util.ChainCodeSize)
        {
            throw new InvalidOperationException($"A chain code must be {Bip32Util.ChainCodeSize} bytes to derive child keys, not {Chaincode?.Length ?? 0}.");
        }

        Span<byte> z = stackalloc byte[64];
        byte[] childChaincode = new byte[Bip32Util.ChainCodeSize];
        Bip32Util.DeriveChildHmac(Key, Chaincode, (uint)index, z, childChaincode);
        byte[] zPoint = Bip32Util.PointOfTrunc28Mul8(z[..32]);
        try
        {
            return new PublicKey(Ed25519.PointAdd(Key, zPoint), childChaincode);
        }
        catch (ArgumentException ex) when (ex.ParamName == "firstPoint")
        {
            throw new InvalidOperationException("The public key is not a valid canonical Ed25519 curve point.", ex);
        }
    }

    /// <summary>
    /// Derives a soft child public key using a CIP-1852 role type.
    /// </summary>
    /// <param name="roleType">The defined CIP-1852 role type.</param>
    /// <returns>The derived role-level public key.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="roleType"/> is not a defined <see cref="RoleType"/> value.</exception>
    /// <exception cref="InvalidOperationException">This key is not a valid 32-byte canonical Ed25519 extended public key with a 32-byte chain code.</exception>
    public PublicKey Derive(RoleType roleType)
    {
        if (!Enum.IsDefined(roleType))
        {
            throw new ArgumentOutOfRangeException(nameof(roleType), roleType, "Must be a defined CIP-1852 role type.");
        }

        return Derive((int)roleType);
    }

    /// <summary>
    /// Derives a payment public key at role=ExternalChain for the given index.
    /// Intended to be called on an account-level extended public key.
    /// </summary>
    /// <param name="index">The address index (default 0).</param>
    /// <returns>The payment public key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">On a valid extended public key, <paramref name="index"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">This key is not a valid 32-byte canonical Ed25519 extended public key with a 32-byte chain code.</exception>
    public PublicKey DerivePaymentKey(int index = 0) =>
        Derive(RoleType.ExternalChain).Derive(index);

    /// <summary>
    /// Derives a stake public key at role=Staking for the given index.
    /// Intended to be called on an account-level extended public key.
    /// </summary>
    /// <param name="index">The address index (default 0).</param>
    /// <returns>The staking public key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">On a valid extended public key, <paramref name="index"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">This key is not a valid 32-byte canonical Ed25519 extended public key with a 32-byte chain code.</exception>
    public PublicKey DeriveStakeKey(int index = 0) =>
        Derive(RoleType.Staking).Derive(index);

    /// <summary>
    /// Verifies a message signature using the Ed25519 algorithm.
    /// </summary>
    /// <param name="message">The original message bytes.</param>
    /// <param name="signature">The signature bytes to verify.</param>
    /// <returns>True if the signature is valid; otherwise false.</returns>
    public bool Verify(byte[] message, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(signature);

        return Ed25519.Verify(signature, message, Key);
    }

    /// <summary>
    /// Returns the hexadecimal string representation of the public key.
    /// </summary>
    /// <returns>The hex-encoded public key string.</returns>
    public string ToHex() => Convert.ToHexString(Key);

    /// <summary>
    /// Computes the Blake2b-224 hash of the public key bytes.
    /// </summary>
    /// <returns>The 28-byte Blake2b-224 hash.</returns>
    public byte[] ToBlake2b224() => HashUtil.Blake2b224(Key);

    /// <summary>
    /// Returns the hex-encoded Blake2b-224 hash of the public key (the key hash).
    /// </summary>
    /// <returns>The 56-character lowercase hex key hash string.</returns>
    public string ToKeyHashHex() => Convert.ToHexStringLower(ToBlake2b224());

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }

        PublicKey other = (PublicKey)obj;
        return Key.SequenceEqual(other.Key) && Chaincode.SequenceEqual(other.Chaincode);
    }

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Convert.ToHexString(Key), Convert.ToHexString(Chaincode));
}

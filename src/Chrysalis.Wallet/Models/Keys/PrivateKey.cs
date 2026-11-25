// Contains code adapted from CardanoSharp.Wallet (MIT), Copyright (c) 2021 CardanoSharp; changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Security.Cryptography;
using Chrysalis.Crypto;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.Models.Keys;

/// <summary>
/// Represents a Cardano private key with support for BIP-32 hierarchical derivation.
/// </summary>
/// <param name="key">A 32-byte Ed25519 seed, or a 64-byte Cardano extended key (scalar followed by prefix).</param>
/// <param name="chaincode">The 32-byte chain code for key derivation.</param>
public class PrivateKey(byte[] key, byte[] chaincode)
{
    /// <summary>
    /// Gets the private key bytes.
    /// </summary>
    public byte[] Key { get; } = Checked(key);

    /// <summary>
    /// Gets the chain code bytes used for hierarchical derivation.
    /// </summary>
    public byte[] Chaincode { get; } = chaincode;

    private static byte[] Checked(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Length == Ed25519.PrivateKeySeedSizeInBytes || key.Length == Ed25519.ExpandedPrivateKeySizeInBytes
            ? key
            : throw new ArgumentException($"Must be {Ed25519.PrivateKeySeedSizeInBytes} or {Ed25519.ExpandedPrivateKeySizeInBytes} bytes, not {key.Length}.", nameof(key));
    }

    /// <summary>
    /// Derives the corresponding public key from this private key.
    /// Returns an empty chain code for a 32-byte Ed25519 seed (which has no child keys), and this key's chain code for a 64-byte Cardano extended key.
    /// </summary>
    /// <returns>The derived public key.</returns>
    public PublicKey GetPublicKey() => Key.Length == Ed25519.PrivateKeySeedSizeInBytes
        ? new(Ed25519.PublicKeyFromSeed(Key), [])
        : new(Ed25519.PublicKeyFromExtendedKey(Key), Chaincode);

    /// <summary>
    /// Derives a child key at the specified index.
    /// </summary>
    /// <param name="index">The derivation index, 0 to 2^31 - 1; <paramref name="type"/> sets the hardened bit.</param>
    /// <param name="type">The derivation type (hard or soft).</param>
    /// <returns>The derived child private key.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">This key is a 32-byte seed, or its chain code is <see langword="null"/> or not 32 bytes.</exception>
    public PrivateKey Derive(int index, DerivationType type = DerivationType.SOFT)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        uint derivationIndex = (uint)index;
        if (type == DerivationType.HARD)
        {
            derivationIndex |= Bip32Util.FirstHardenedIndex;
        }

        return GetChildKeyDerivation(derivationIndex);
    }

    /// <summary>
    /// Derives a child key using a purpose type.
    /// </summary>
    /// <param name="purpose">The BIP-44 purpose type.</param>
    /// <param name="type">The derivation type (hard or soft).</param>
    /// <returns>The derived child private key.</returns>
    /// <exception cref="InvalidOperationException">This key is a 32-byte seed, or its chain code is <see langword="null"/> or not 32 bytes.</exception>
    public PrivateKey Derive(PurposeType purpose, DerivationType type = DerivationType.SOFT) => Derive((int)purpose, type);

    /// <summary>
    /// Derives a child key using a coin type.
    /// </summary>
    /// <param name="coinType">The BIP-44 coin type.</param>
    /// <param name="type">The derivation type (hard or soft).</param>
    /// <returns>The derived child private key.</returns>
    /// <exception cref="InvalidOperationException">This key is a 32-byte seed, or its chain code is <see langword="null"/> or not 32 bytes.</exception>
    public PrivateKey Derive(CoinType coinType, DerivationType type = DerivationType.SOFT) => Derive((int)coinType, type);

    /// <summary>
    /// Derives a child key using a role type.
    /// </summary>
    /// <param name="roleType">The CIP-1852 role type.</param>
    /// <param name="type">The derivation type (hard or soft).</param>
    /// <returns>The derived child private key.</returns>
    /// <exception cref="InvalidOperationException">This key is a 32-byte seed, or its chain code is <see langword="null"/> or not 32 bytes.</exception>
    public PrivateKey Derive(RoleType roleType, DerivationType type = DerivationType.SOFT) => Derive((int)roleType, type);

    /// <summary>
    /// Derives the Cardano account key at path m/1852'/1815'/<paramref name="account"/>'.
    /// </summary>
    /// <param name="account">The account index (default 0).</param>
    /// <returns>The account-level private key.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="account"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">This key is a 32-byte seed, or its chain code is <see langword="null"/> or not 32 bytes.</exception>
    public PrivateKey DeriveCardanoAccountKey(int account = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(account);
        return Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(account, DerivationType.HARD);
    }

    /// <summary>
    /// Derives a payment key at role=ExternalChain for the given index.
    /// Intended to be called on an account-level key.
    /// </summary>
    /// <param name="index">The address index (default 0).</param>
    /// <returns>The payment private key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">On an extended key, <paramref name="index"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">This key is a 32-byte seed, or its chain code is <see langword="null"/> or not 32 bytes.</exception>
    public PrivateKey DerivePaymentKey(int index = 0) =>
        Derive(RoleType.ExternalChain).Derive(index);

    /// <summary>
    /// Derives a stake key at role=Staking for the given index.
    /// Intended to be called on an account-level key.
    /// </summary>
    /// <param name="index">The address index (default 0).</param>
    /// <returns>The staking private key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">On an extended key, <paramref name="index"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">This key is a 32-byte seed, or its chain code is <see langword="null"/> or not 32 bytes.</exception>
    public PrivateKey DeriveStakeKey(int index = 0) =>
        Derive(RoleType.Staking).Derive(index);

    private PrivateKey GetChildKeyDerivation(uint index)
    {
        if (Key.Length != Ed25519.ExpandedPrivateKeySizeInBytes)
        {
            throw new InvalidOperationException("Only a 64-byte Cardano extended key derives child keys; this key is a 32-byte Ed25519 seed.");
        }

        if (Chaincode is null || Chaincode.Length != Bip32Util.ChainCodeSize)
        {
            throw new InvalidOperationException($"A chain code must be {Bip32Util.ChainCodeSize} bytes to derive child keys, not {Chaincode?.Length ?? 0}.");
        }

        Span<byte> z = stackalloc byte[64];
        byte[] derivedKey = new byte[Ed25519.ExpandedPrivateKeySizeInBytes];
        byte[] cc = new byte[Bip32Util.ChainCodeSize];
        try
        {
            ReadOnlySpan<byte> material = Bip32Util.FromIndex(index) == DerivationType.HARD ? Key : GetPublicKey().Key;
            Bip32Util.DeriveChildHmac(material, Chaincode, index, z, cc);
            Bip32Util.Add28Mul8(Key.AsSpan(0, 32), z[..32], derivedKey.AsSpan(0, 32));
            Bip32Util.Add256Bits(Key.AsSpan(32, 32), z[32..], derivedKey.AsSpan(32, 32));
            return new PrivateKey(derivedKey, cc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(z);
        }
    }

    /// <summary>
    /// Signs a message using the Ed25519 algorithm.
    /// </summary>
    /// <param name="message">The message bytes to sign.</param>
    /// <returns>The Ed25519 signature bytes.</returns>
    public byte[] Sign(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (Key.Length != Ed25519.PrivateKeySeedSizeInBytes)
        {
            return Ed25519.SignWithExtendedKey(message, Key);
        }

        byte[] seedAndPublicKey = Ed25519.SeedAndPublicKeyFromSeed(Key);
        try
        {
            return Ed25519.SignWithSeedAndPublicKey(message, seedAndPublicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seedAndPublicKey);
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }

        PrivateKey other = (PrivateKey)obj;
        return Key.SequenceEqual(other.Key) && Chaincode.SequenceEqual(other.Chaincode);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.AddBytes(Key);
        hash.AddBytes(Chaincode);
        return hash.ToHashCode();
    }
}

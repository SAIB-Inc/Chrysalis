// Contains code adapted from Chaos.NaCl (public domain); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Chrysalis.Crypto.Internal.Ed25519Ref10;

namespace Chrysalis.Crypto;

/// <summary>
/// Provides Ed25519 digital signature operations including key generation, signing, and verification.
/// </summary>
/// <remarks>
/// A private key comes in one of three formats, and each member names the one it takes:
/// a 32-byte seed (RFC 8032);
/// 64 bytes of seed followed by public key, as <see cref="SeedAndPublicKeyFromSeed(byte[])"/> returns;
/// 64 bytes of Cardano scalar followed by prefix, the extended key of BIP32-Ed25519 derivation.
/// No member tells the two 64-byte formats apart by length.
/// </remarks>
public static class Ed25519
{
    /// <summary>
    /// The size of an Ed25519 public key in bytes (32).
    /// </summary>
    public static readonly int PublicKeySizeInBytes = 32;

    /// <summary>
    /// The size of an Ed25519 signature in bytes (64).
    /// </summary>
    public static readonly int SignatureSizeInBytes = 64;

    /// <summary>
    /// The size in bytes (64) of both 64-byte private key formats: seed followed by public key, and Cardano scalar followed by prefix.
    /// </summary>
    public static readonly int ExpandedPrivateKeySizeInBytes = 32 * 2;

    /// <summary>
    /// The size of an Ed25519 private key seed in bytes (32).
    /// </summary>
    public static readonly int PrivateKeySeedSizeInBytes = 32;

    /// <summary>
    /// Verifies an Ed25519 signature against a message and public key.
    /// </summary>
    /// <param name="signature">The 64-byte signature.</param>
    /// <param name="message">The message that was signed.</param>
    /// <param name="publicKey">The 32-byte public key.</param>
    /// <returns><see langword="true"/> if the signature is valid; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Matches the Cardano node's cofactorless Ed25519 verifier: RFC 8032 §5.1.7 requires
    /// 0 &lt;= S &lt; L and canonical A and R encodings; small-order A and R are also rejected.
    /// </remarks>
    public static bool Verify(byte[] signature, byte[] message, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publicKey);

        return Verify(signature.AsSpan(), message.AsSpan(), publicKey.AsSpan());
    }

    /// <summary>
    /// Verifies an Ed25519 signature against a message and public key.
    /// </summary>
    /// <param name="signature">The 64-byte signature.</param>
    /// <param name="message">The message that was signed.</param>
    /// <param name="publicKey">The 32-byte public key.</param>
    /// <returns><see langword="true"/> if the signature is valid; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Matches the Cardano node's cofactorless Ed25519 verifier: RFC 8032 §5.1.7 requires
    /// 0 &lt;= S &lt; L and canonical A and R encodings; small-order A and R are also rejected.
    /// </remarks>
    [OverloadResolutionPriority(-1)]
    public static bool Verify(ReadOnlySpan<byte> signature, ReadOnlySpan<byte> message, ReadOnlySpan<byte> publicKey)
    {
        if (signature.Length != SignatureSizeInBytes)
            throw new ArgumentException($"Signature size must be {SignatureSizeInBytes}", nameof(signature));
        if (publicKey.Length != PublicKeySizeInBytes)
            throw new ArgumentException($"Public key size must be {PublicKeySizeInBytes}", nameof(publicKey));

        return Ed25519Operations.crypto_sign_verify(signature, message, publicKey);
    }

    /// <summary>
    /// Returns the RFC 8032 public key of a 32-byte seed.
    /// </summary>
    /// <param name="seed">The 32-byte seed.</param>
    /// <returns>The 32-byte public key.</returns>
    public static byte[] PublicKeyFromSeed(byte[] seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        return PublicKeyFromSeed(seed.AsSpan());
    }

    /// <summary>
    /// Returns the RFC 8032 public key of a 32-byte seed.
    /// </summary>
    /// <param name="seed">The 32-byte seed.</param>
    /// <returns>The 32-byte public key.</returns>
    public static byte[] PublicKeyFromSeed(ReadOnlySpan<byte> seed)
    {
        RequireLength(seed, PrivateKeySeedSizeInBytes, nameof(seed));
        Span<byte> seedAndPublicKey = stackalloc byte[ExpandedPrivateKeySizeInBytes];
        byte[] publicKey = new byte[PublicKeySizeInBytes];
        try
        {
            Ed25519Operations.crypto_sign_keypair(publicKey, seedAndPublicKey, seed);
            return publicKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seedAndPublicKey);
        }
    }

    /// <summary>
    /// Returns 64 bytes of seed followed by the seed's public key.
    /// </summary>
    /// <param name="seed">The 32-byte seed.</param>
    /// <returns>The seed followed by its 32-byte public key.</returns>
    public static byte[] SeedAndPublicKeyFromSeed(byte[] seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        return SeedAndPublicKeyFromSeed(seed.AsSpan());
    }

    /// <summary>
    /// Returns 64 bytes of seed followed by the seed's public key.
    /// </summary>
    /// <param name="seed">The 32-byte seed.</param>
    /// <returns>The seed followed by its 32-byte public key.</returns>
    public static byte[] SeedAndPublicKeyFromSeed(ReadOnlySpan<byte> seed)
    {
        RequireLength(seed, PrivateKeySeedSizeInBytes, nameof(seed));
        Span<byte> publicKey = stackalloc byte[PublicKeySizeInBytes];
        byte[] seedAndPublicKey = new byte[ExpandedPrivateKeySizeInBytes];
        try
        {
            Ed25519Operations.crypto_sign_keypair(publicKey, seedAndPublicKey, seed);
            return seedAndPublicKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    /// <summary>
    /// Signs a message (RFC 8032) with 64 bytes of seed followed by public key.
    /// The public key must be the seed's own, as <see cref="SeedAndPublicKeyFromSeed(byte[])"/> returns: a signature under another public key does not verify, and two signatures of one message under two different public keys reveal the secret scalar.
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="seedAndPublicKey">The 64-byte seed followed by public key.</param>
    /// <returns>The 64-byte signature.</returns>
    public static byte[] SignWithSeedAndPublicKey(byte[] message, byte[] seedAndPublicKey)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(seedAndPublicKey);
        return SignWithSeedAndPublicKey(message.AsSpan(), seedAndPublicKey.AsSpan());
    }

    /// <summary>
    /// Signs a message (RFC 8032) with 64 bytes of seed followed by public key.
    /// The public key must be the seed's own, as <see cref="SeedAndPublicKeyFromSeed(byte[])"/> returns: a signature under another public key does not verify, and two signatures of one message under two different public keys reveal the secret scalar.
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="seedAndPublicKey">The 64-byte seed followed by public key.</param>
    /// <returns>The 64-byte signature.</returns>
    [OverloadResolutionPriority(-1)]
    public static byte[] SignWithSeedAndPublicKey(ReadOnlySpan<byte> message, ReadOnlySpan<byte> seedAndPublicKey)
    {
        RequireLength(seedAndPublicKey, ExpandedPrivateKeySizeInBytes, nameof(seedAndPublicKey));
        byte[] signature = new byte[SignatureSizeInBytes];
        Ed25519Operations.crypto_sign2(signature, message, seedAndPublicKey);
        return signature;
    }

    /// <summary>
    /// Signs a message with a 64-byte Cardano extended key (scalar followed by prefix).
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="extendedKey">The 64-byte extended key.</param>
    /// <returns>The 64-byte signature.</returns>
    public static byte[] SignWithExtendedKey(byte[] message, byte[] extendedKey)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(extendedKey);
        return SignWithExtendedKey(message.AsSpan(), extendedKey.AsSpan());
    }

    /// <summary>
    /// Signs a message with a 64-byte Cardano extended key (scalar followed by prefix).
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="extendedKey">The 64-byte extended key.</param>
    /// <returns>The 64-byte signature.</returns>
    [OverloadResolutionPriority(-1)]
    public static byte[] SignWithExtendedKey(ReadOnlySpan<byte> message, ReadOnlySpan<byte> extendedKey)
    {
        RequireLength(extendedKey, ExpandedPrivateKeySizeInBytes, nameof(extendedKey));
        byte[] signature = new byte[SignatureSizeInBytes];
        Ed25519Operations.crypto_sign3(signature, message, extendedKey);
        return signature;
    }

    /// <summary>
    /// Returns the public key of a 64-byte Cardano extended key (scalar followed by prefix).
    /// </summary>
    /// <param name="extendedKey">The 64-byte extended key.</param>
    /// <returns>The 32-byte public key.</returns>
    public static byte[] PublicKeyFromExtendedKey(byte[] extendedKey)
    {
        ArgumentNullException.ThrowIfNull(extendedKey);
        return PublicKeyFromExtendedKey(extendedKey.AsSpan());
    }

    /// <summary>
    /// Returns the public key of a 64-byte Cardano extended key (scalar followed by prefix).
    /// </summary>
    /// <param name="extendedKey">The 64-byte extended key.</param>
    /// <returns>The 32-byte public key.</returns>
    public static byte[] PublicKeyFromExtendedKey(ReadOnlySpan<byte> extendedKey)
    {
        RequireLength(extendedKey, ExpandedPrivateKeySizeInBytes, nameof(extendedKey));
        return GetPublicKey(extendedKey);
    }

    /// <summary>
    /// Signs a message with 64 bytes of seed followed by public key; the same as <see cref="SignWithSeedAndPublicKey(byte[], byte[])"/>.
    /// The public key must be the seed's own, as <see cref="ExpandedPrivateKeyFromSeed"/> returns: a signature under another public key does not verify, and two signatures of one message under two different public keys reveal the secret scalar.
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="expandedPrivateKey">The 64-byte seed followed by public key.</param>
    /// <returns>The 64-byte Ed25519 signature.</returns>
    public static byte[] Sign(byte[] message, byte[] expandedPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(message);
        RequireLength(expandedPrivateKey, ExpandedPrivateKeySizeInBytes, nameof(expandedPrivateKey));
        return SignWithSeedAndPublicKey(message, expandedPrivateKey);
    }

    /// <summary>
    /// Signs a message with a 64-byte Cardano extended key (scalar followed by prefix); the same as <see cref="SignWithExtendedKey(byte[], byte[])"/>.
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="expandedPrivateKey">The 64-byte extended key.</param>
    /// <returns>The 64-byte Ed25519 signature.</returns>
    public static byte[] SignCrypto(byte[] message, byte[] expandedPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(message);
        RequireLength(expandedPrivateKey, ExpandedPrivateKeySizeInBytes, nameof(expandedPrivateKey));
        return SignWithExtendedKey(message, expandedPrivateKey);
    }

    /// <summary>
    /// Returns 64 bytes of seed followed by public key; the same as <see cref="SeedAndPublicKeyFromSeed(byte[])"/>.
    /// </summary>
    /// <param name="privateKeySeed">The 32-byte seed.</param>
    /// <returns>The 64-byte seed followed by public key.</returns>
    public static byte[] ExpandedPrivateKeyFromSeed(byte[] privateKeySeed)
    {
        RequireLength(privateKeySeed, PrivateKeySeedSizeInBytes, nameof(privateKeySeed));
        return SeedAndPublicKeyFromSeed(privateKeySeed);
    }

    /// <summary>
    /// Returns the point of a ready scalar: the first 32 bytes of a 64-byte Cardano extended key,
    /// or a 32-byte scalar such as the one Bip32Util.PointOfTrunc28Mul8 passes.
    /// Not for a seed: use <see cref="PublicKeyFromSeed(byte[])"/>.
    /// </summary>
    /// <param name="expandedPrivateKey">A 32-byte scalar or a 64-byte extended key.</param>
    /// <returns>The 32-byte public key.</returns>
    public static byte[] GetPublicKey(byte[] expandedPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(expandedPrivateKey);
        return GetPublicKey(expandedPrivateKey.AsSpan());
    }

    /// <summary>
    /// Returns the point of a ready scalar: the first 32 bytes of a 64-byte Cardano extended key,
    /// or a 32-byte scalar such as the one Bip32Util.PointOfTrunc28Mul8 passes.
    /// Not for a seed: use <see cref="PublicKeyFromSeed(byte[])"/>.
    /// </summary>
    /// <param name="expandedPrivateKey">A 32-byte scalar or a 64-byte extended key.</param>
    /// <returns>The 32-byte public key.</returns>
    public static byte[] GetPublicKey(ReadOnlySpan<byte> expandedPrivateKey)
    {
        if (expandedPrivateKey.Length != PrivateKeySeedSizeInBytes && expandedPrivateKey.Length != ExpandedPrivateKeySizeInBytes)
        {
            throw new ArgumentException($"Must be {PrivateKeySeedSizeInBytes} or {ExpandedPrivateKeySizeInBytes} bytes, not {expandedPrivateKey.Length}.", nameof(expandedPrivateKey));
        }

        byte[] pk = new byte[PublicKeySizeInBytes];
        Ed25519Operations.crypto_get_pubkey(pk, expandedPrivateKey);
        return pk;
    }

    /// <summary>
    /// Adds two canonical Ed25519 curve points in RFC 8032 compressed 32-byte form.
    /// </summary>
    /// <param name="firstPoint">The first 32-byte canonical Ed25519 curve point.</param>
    /// <param name="secondPoint">The second 32-byte canonical Ed25519 curve point.</param>
    /// <returns>The 32-byte canonical encoding of the point sum.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="firstPoint"/> or <paramref name="secondPoint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An input is not 32 bytes, is off-curve, or is not canonically encoded per RFC 8032.</exception>
    public static byte[] PointAdd(byte[] firstPoint, byte[] secondPoint)
    {
        RequireLength(firstPoint, PublicKeySizeInBytes, nameof(firstPoint));
        RequireLength(secondPoint, PublicKeySizeInBytes, nameof(secondPoint));
        return PointAdd(firstPoint.AsSpan(), secondPoint.AsSpan());
    }

    /// <summary>
    /// Adds two canonical Ed25519 curve points in RFC 8032 compressed 32-byte form.
    /// </summary>
    /// <param name="firstPoint">The first 32-byte canonical Ed25519 curve point.</param>
    /// <param name="secondPoint">The second 32-byte canonical Ed25519 curve point.</param>
    /// <returns>The 32-byte canonical encoding of the point sum.</returns>
    /// <exception cref="ArgumentException">An input is not 32 bytes, is off-curve, or is not canonically encoded per RFC 8032.</exception>
    [OverloadResolutionPriority(-1)]
    public static byte[] PointAdd(ReadOnlySpan<byte> firstPoint, ReadOnlySpan<byte> secondPoint)
    {
        RequireLength(firstPoint, PublicKeySizeInBytes, nameof(firstPoint));
        RequireLength(secondPoint, PublicKeySizeInBytes, nameof(secondPoint));

        byte[] sum = new byte[PublicKeySizeInBytes];
        int status = Ed25519Operations.crypto_point_plus(firstPoint, secondPoint, sum);
        if (status != 0)
        {
            string paramName = status == 1 ? nameof(firstPoint) : nameof(secondPoint);
            throw new ArgumentException("Must be a valid canonical Ed25519 curve point.", paramName);
        }

        return sum;
    }

    private static void RequireLength(byte[] key, int length, string keyName)
    {
        ArgumentNullException.ThrowIfNull(key, keyName);
        RequireLength(key.AsSpan(), length, keyName);
    }

    private static void RequireLength(ReadOnlySpan<byte> key, int length, string keyName)
    {
        if (key.Length != length)
        {
            throw new ArgumentException($"Must be {length} bytes, not {key.Length}.", keyName);
        }
    }
}

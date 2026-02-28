using Chrysalis.Crypto.Internal.Ed25519Ref10;

namespace Chrysalis.Crypto;

/// <summary>
/// Provides Ed25519 digital signature operations including key generation, signing, and verification.
/// </summary>
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
    /// The size of an expanded Ed25519 private key in bytes (64).
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
    public static bool Verify(byte[] signature, byte[] message, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (signature.Length != SignatureSizeInBytes)
            throw new ArgumentException($"Signature size must be {SignatureSizeInBytes}", nameof(signature));
        if (publicKey.Length != PublicKeySizeInBytes)
            throw new ArgumentException($"Public key size must be {PublicKeySizeInBytes}", nameof(publicKey));

        return Ed25519Operations.crypto_sign_verify(signature, 0, message, 0, message.Length, publicKey, 0);
    }

    /// <summary>
    /// Signs a message using a standard expanded private key (seed-derived).
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="expandedPrivateKey">The 64-byte expanded private key.</param>
    /// <returns>The 64-byte Ed25519 signature.</returns>
    public static byte[] Sign(byte[] message, byte[] expandedPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expandedPrivateKey);

        byte[] signature = new byte[SignatureSizeInBytes];
        SignInternal(signature, 0, message, 0, message.Length, expandedPrivateKey, 0, forceSmallOrder: true);
        return signature;
    }

    /// <summary>
    /// Signs a message using Cardano-style extended key signing (BIP32-Ed25519).
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="expandedPrivateKey">The 64-byte expanded private key.</param>
    /// <returns>The 64-byte Ed25519 signature.</returns>
    public static byte[] SignCrypto(byte[] message, byte[] expandedPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(expandedPrivateKey);

        byte[] signature = new byte[SignatureSizeInBytes];
        Ed25519Operations.crypto_sign3(signature, message, expandedPrivateKey);
        return signature;
    }

    /// <summary>
    /// Expands a 32-byte private key seed into a 64-byte expanded private key.
    /// </summary>
    /// <param name="privateKeySeed">The 32-byte private key seed.</param>
    /// <returns>The 64-byte expanded private key.</returns>
    public static byte[] ExpandedPrivateKeyFromSeed(byte[] privateKeySeed)
    {
        ArgumentNullException.ThrowIfNull(privateKeySeed);

        if (privateKeySeed.Length != PrivateKeySeedSizeInBytes)
            throw new ArgumentException($"Private key seed must be {PrivateKeySeedSizeInBytes} bytes", nameof(privateKeySeed));

        byte[] publicKey = new byte[PublicKeySizeInBytes];
        byte[] expandedPrivateKey = new byte[ExpandedPrivateKeySizeInBytes];
        Ed25519Operations.crypto_sign_keypair(publicKey, 0, expandedPrivateKey, 0, privateKeySeed, 0);
        CryptoBytes.Wipe(publicKey);
        return expandedPrivateKey;
    }

    /// <summary>
    /// Derives the public key from an expanded private key.
    /// </summary>
    /// <param name="expandedPrivateKey">The 64-byte expanded private key.</param>
    /// <returns>The 32-byte public key.</returns>
    public static byte[] GetPublicKey(byte[] expandedPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(expandedPrivateKey);

        byte[] sk = new byte[expandedPrivateKey.Length];
        Buffer.BlockCopy(expandedPrivateKey, 0, sk, 0, expandedPrivateKey.Length);
        byte[] pk = new byte[PublicKeySizeInBytes];
        Ed25519Operations.crypto_get_pubkey(pk, sk);
        return pk;
    }

    private static void SignInternal(
        byte[] signature, int sigOffset,
        byte[] message, int msgOffset, int msgLength,
        byte[] expandedPrivateKey, int skOffset,
        bool forceSmallOrder)
    {
        if (forceSmallOrder)
        {
            Ed25519Operations.crypto_sign2(signature, sigOffset, message, msgOffset, msgLength, expandedPrivateKey, skOffset);
        }
        else
        {
            Ed25519Operations.crypto_sign3(signature, message, expandedPrivateKey);
        }
    }
}

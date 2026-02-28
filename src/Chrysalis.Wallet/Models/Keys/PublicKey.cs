using Chrysalis.Crypto;

namespace Chrysalis.Wallet.Models.Keys;

/// <summary>
/// Represents a Cardano Ed25519 public key with an associated chain code.
/// </summary>
/// <param name="key">The 32-byte public key bytes.</param>
/// <param name="chaincode">The 32-byte chain code for key derivation.</param>
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
    public string ToHex()
    {
        return Convert.ToHexString(Key);
    }

    /// <summary>
    /// Computes the Blake2b-224 hash of the public key bytes.
    /// </summary>
    /// <returns>The 28-byte Blake2b-224 hash.</returns>
    public byte[] ToBlake2b224()
    {
        return Blake2Fast.Blake2b.HashData(28, Key);
    }

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
    public override int GetHashCode()
    {
        return HashCode.Combine(Convert.ToHexString(Key), Convert.ToHexString(Chaincode));
    }
}

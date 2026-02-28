using System.Buffers.Binary;
using System.Security.Cryptography;
using Chaos.NaCl;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.Models.Keys;

/// <summary>
/// Represents a Cardano private key with support for BIP-32 hierarchical derivation.
/// </summary>
/// <param name="key">The private key bytes (32 or 64 bytes).</param>
/// <param name="chaincode">The 32-byte chain code for key derivation.</param>
public class PrivateKey(byte[] key, byte[] chaincode)
{
    /// <summary>
    /// Gets the private key bytes.
    /// </summary>
    public byte[] Key { get; } = key;

    /// <summary>
    /// Gets the chain code bytes used for hierarchical derivation.
    /// </summary>
    public byte[] Chaincode { get; } = chaincode;

    private const uint MinHardIndex = 0x80000000;

    /// <summary>
    /// Derives the corresponding public key from this private key.
    /// </summary>
    /// <returns>The derived public key.</returns>
    public PublicKey GetPublicKey()
    {
        byte[] sk = new byte[Key.Length];
        Buffer.BlockCopy(Key, 0, sk, 0, Key.Length);
        byte[] pk = Ed25519.GetPublicKey(sk);
        return new PublicKey(pk, Chaincode);
    }

    /// <summary>
    /// Derives a child key at the specified index.
    /// </summary>
    /// <param name="index">The derivation index.</param>
    /// <param name="type">The derivation type (hard or soft).</param>
    /// <returns>The derived child private key.</returns>
    public PrivateKey Derive(int index, DerivationType type = DerivationType.SOFT)
    {
        uint derivationIndex = (uint)index;
        if (type == DerivationType.HARD)
        {
            derivationIndex |= MinHardIndex;
        }

        return GetChildKeyDerivation(derivationIndex);
    }

    /// <summary>
    /// Derives a child key using a purpose type.
    /// </summary>
    /// <param name="purpose">The BIP-44 purpose type.</param>
    /// <param name="type">The derivation type (hard or soft).</param>
    /// <returns>The derived child private key.</returns>
    public PrivateKey Derive(PurposeType purpose, DerivationType type = DerivationType.SOFT)
    {
        return Derive((int)purpose, type);
    }

    /// <summary>
    /// Derives a child key using a coin type.
    /// </summary>
    /// <param name="coinType">The BIP-44 coin type.</param>
    /// <param name="type">The derivation type (hard or soft).</param>
    /// <returns>The derived child private key.</returns>
    public PrivateKey Derive(CoinType coinType, DerivationType type = DerivationType.SOFT)
    {
        return Derive((int)coinType, type);
    }

    /// <summary>
    /// Derives a child key using a role type.
    /// </summary>
    /// <param name="roleType">The CIP-1852 role type.</param>
    /// <param name="type">The derivation type (hard or soft).</param>
    /// <returns>The derived child private key.</returns>
    public PrivateKey Derive(RoleType roleType, DerivationType type = DerivationType.SOFT)
    {
        return Derive((int)roleType, type);
    }

    private PrivateKey GetChildKeyDerivation(ulong index)
    {
        // Split the 64-byte private key into two 32-byte halves.
        byte[] kl = new byte[32];
        Buffer.BlockCopy(Key, 0, kl, 0, 32);
        byte[] kr = new byte[32];
        Buffer.BlockCopy(Key, 32, kr, 0, 32);

        byte[] z;   // HMAC result for z (64 bytes)
        byte[] zl;  // first half of z
        byte[] zr;  // second half of z
        byte[] i;   // HMAC result for i (64 bytes)
        byte[] seri = Bip32Util.Le32(index);

        byte[] zBufferArr;
        byte[] iBufferArr;
        Span<byte> temp = stackalloc byte[2];

        if (Bip32Util.FromIndex(index) == DerivationType.HARD)
        {
            int totalLen = 1 + Key.Length + seri.Length;
            zBufferArr = new byte[totalLen];
            iBufferArr = new byte[totalLen];

            BinaryPrimitives.WriteUInt16BigEndian(temp, 0x0000);
            zBufferArr[0] = temp[1];

            BinaryPrimitives.WriteUInt16BigEndian(temp, 0x0001);
            iBufferArr[0] = temp[1];

            Buffer.BlockCopy(Key, 0, zBufferArr, 1, Key.Length);
            Buffer.BlockCopy(Key, 0, iBufferArr, 1, Key.Length);
            Buffer.BlockCopy(seri, 0, zBufferArr, 1 + Key.Length, seri.Length);
            Buffer.BlockCopy(seri, 0, iBufferArr, 1 + Key.Length, seri.Length);
        }
        else
        {
            PublicKey pk = GetPublicKey();
            int totalLen = 1 + pk.Key.Length + seri.Length;
            zBufferArr = new byte[totalLen];
            iBufferArr = new byte[totalLen];

            BinaryPrimitives.WriteUInt16BigEndian(temp, 0x0002);
            zBufferArr[0] = temp[1];

            BinaryPrimitives.WriteUInt16BigEndian(temp, 0x0003);
            iBufferArr[0] = temp[1];

            Buffer.BlockCopy(pk.Key, 0, zBufferArr, 1, pk.Key.Length);
            Buffer.BlockCopy(pk.Key, 0, iBufferArr, 1, pk.Key.Length);
            Buffer.BlockCopy(seri, 0, zBufferArr, 1 + pk.Key.Length, seri.Length);
            Buffer.BlockCopy(seri, 0, iBufferArr, 1 + pk.Key.Length, seri.Length);
        }

        using (HMACSHA512 hmacSha512 = new(Chaincode))
        {
            z = hmacSha512.ComputeHash(zBufferArr);
            zl = z[..32];
            zr = z[32..];
        }

        byte[] left = Bip32Util.Add28Mul8(kl, zl);
        byte[] right = Bip32Util.Add256Bits(kr, zr);

        byte[] derivedKey = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, derivedKey, 0, left.Length);
        Buffer.BlockCopy(right, 0, derivedKey, left.Length, right.Length);

        using (HMACSHA512 hmacSha512 = new(Chaincode))
        {
            i = hmacSha512.ComputeHash(iBufferArr);
        }
        byte[] cc = i[32..];

        return new PrivateKey(derivedKey, cc);
    }

    /// <summary>
    /// Signs a message using the Ed25519 algorithm.
    /// </summary>
    /// <param name="message">The message bytes to sign.</param>
    /// <returns>The Ed25519 signature bytes.</returns>
    public byte[] Sign(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] skey = Key;
        if (skey.Length == 32)
        {
            skey = Ed25519.ExpandedPrivateKeyFromSeed(skey);
            return Ed25519.Sign(message, skey);
        }
        return Ed25519.SignCrypto(message, skey);
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
        return HashCode.Combine(Convert.ToHexString(Key), Convert.ToHexString(Chaincode));
    }
}

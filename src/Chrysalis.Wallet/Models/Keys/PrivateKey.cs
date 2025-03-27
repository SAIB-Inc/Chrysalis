using System.Buffers.Binary;
using System.Security.Cryptography;
using Chaos.NaCl;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.Models.Keys;

public class PrivateKey(byte[] key, byte[] chaincode)
{
    public byte[] Key { get; } = key;
    public byte[] Chaincode { get; } = chaincode;

    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
            return false;

        PrivateKey other = (PrivateKey)obj;
        return Key.SequenceEqual(other.Key) && Chaincode.SequenceEqual(other.Chaincode);
    }

    public override int GetHashCode() => HashCode.Combine(Convert.ToHexString(Key), Convert.ToHexString(Chaincode));
}

public static class PrivateKeyExtensions
{
    private static readonly uint MinHardIndex = 0x80000000;

    public static PublicKey GetPublicKey(this PrivateKey privateKey)
    {
        byte[] sk = new byte[privateKey.Key.Length];
        Buffer.BlockCopy(privateKey.Key, 0, sk, 0, privateKey.Key.Length);
        byte[] pk = Ed25519.GetPublicKey(sk);

        byte[] buffer = new byte[pk.Length];
        pk.CopyTo(buffer, 0);

        return new PublicKey([.. buffer], privateKey.Chaincode);
    }

    public static PrivateKey Derive(this PrivateKey privateKey, int index, DerivationType type = DerivationType.SOFT)
    {
        if (privateKey is null)
            throw new ArgumentNullException(nameof(privateKey));

        uint derivationIndex = (uint)index;

        // Adjust index based on the derivation type
        if (type == DerivationType.HARD)
            derivationIndex |= MinHardIndex; // Hardened derivation requires adding the MinHardIndex offset

        // Derive the child key using the BIP32 method
        PrivateKey newPrivateKey = GetChildKeyDerivation(privateKey, derivationIndex);

        // Return the derived key to allow method chaining
        return newPrivateKey;
    }

    public static PrivateKey GetChildKeyDerivation(this PrivateKey privateKey, ulong index)
    {
        // Split the 64-byte private key into two 32-byte halves.
        byte[] kl = new byte[32];
        Buffer.BlockCopy(privateKey.Key, 0, kl, 0, 32);
        byte[] kr = new byte[32];
        Buffer.BlockCopy(privateKey.Key, 32, kr, 0, 32);

        byte[] z;   // HMAC result for z (64 bytes)
        byte[] zl = new byte[32];  // first half of z
        byte[] zr = new byte[32];  // second half of z
        byte[] i;   // HMAC result for i (64 bytes)
        byte[] seri = Bip32Util.Le32(index);

        byte[] zBufferArr;
        byte[] iBufferArr;
        // We use a temporary 2-byte span for BinaryPrimitives writes.
        Span<byte> temp = stackalloc byte[2];

        if (Bip32Util.FromIndex(index) == DerivationType.HARD)
        {
            // Buffer layout: 1 byte constant || privateKey.Key || seri
            int totalLen = 1 + privateKey.Key.Length + seri.Length;
            zBufferArr = new byte[totalLen];
            iBufferArr = new byte[totalLen];

            // Write constant using BinaryPrimitives:
            // Write 0x0000 as a 16-bit big-endian number and take the low-order byte.
            BinaryPrimitives.WriteUInt16BigEndian(temp, 0x0000);
            zBufferArr[0] = temp[1];

            BinaryPrimitives.WriteUInt16BigEndian(temp, 0x0001);
            iBufferArr[0] = temp[1];

            // Append privateKey.Key after the constant.
            Buffer.BlockCopy(privateKey.Key, 0, zBufferArr, 1, privateKey.Key.Length);
            Buffer.BlockCopy(privateKey.Key, 0, iBufferArr, 1, privateKey.Key.Length);

            // Append seri after the key.
            Buffer.BlockCopy(seri, 0, zBufferArr, 1 + privateKey.Key.Length, seri.Length);
            Buffer.BlockCopy(seri, 0, iBufferArr, 1 + privateKey.Key.Length, seri.Length);
        }
        else
        {
            // Soft derivation: use the public key instead.
            PublicKey pk = privateKey.GetPublicKey();
            int totalLen = 1 + pk.Key.Length + seri.Length;
            zBufferArr = new byte[totalLen];
            iBufferArr = new byte[totalLen];

            BinaryPrimitives.WriteUInt16BigEndian(temp, 0x0002);
            zBufferArr[0] = temp[1]; // 0x02

            BinaryPrimitives.WriteUInt16BigEndian(temp, 0x0003);
            iBufferArr[0] = temp[1]; // 0x03

            Buffer.BlockCopy(pk.Key, 0, zBufferArr, 1, pk.Key.Length);
            Buffer.BlockCopy(pk.Key, 0, iBufferArr, 1, pk.Key.Length);

            Buffer.BlockCopy(seri, 0, zBufferArr, 1 + pk.Key.Length, seri.Length);
            Buffer.BlockCopy(seri, 0, iBufferArr, 1 + pk.Key.Length, seri.Length);
        }

        // Compute z = HMACSHA512(zBufferArr)
        using (HMACSHA512 hmacSha512 = new HMACSHA512(privateKey.Chaincode))
        {
            z = hmacSha512.ComputeHash(zBufferArr);
            zl = z[..32];
            zr = z[32..];
        }

        // left = kl + 8 * trunc28(zl)
        byte[] left = Bip32Util.Add28Mul8(kl, zl);
        // right = zr + kr
        byte[] right = Bip32Util.Add256Bits(kr, zr);

        byte[] key = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, key, 0, left.Length);
        Buffer.BlockCopy(right, 0, key, left.Length, right.Length);

        // Compute chaincode from iBufferArr.
        using (HMACSHA512 hmacSha512 = new(privateKey.Chaincode))
        {
            i = hmacSha512.ComputeHash(iBufferArr);
        }
        byte[] cc = i[32..];

        return new PrivateKey(key, cc);
    }

    public static byte[] Sign(this PrivateKey privateKey, byte[] message)
    {
        byte[] skey = privateKey.Key;

        if (skey.Length == 32)
        {
            skey = Ed25519.ExpandedPrivateKeyFromSeed(skey);
            return Ed25519.Sign(message, skey);
        }

        return Ed25519.SignCrypto(message, skey);
    }

    /// <summary>
    /// Derives a child private key using a purpose type enum
    /// </summary>
    public static PrivateKey Derive(this PrivateKey privateKey, PurposeType purposeType, DerivationType type = DerivationType.SOFT)
    {
        int index = (int)purposeType;
        return Derive(privateKey, index, type);
    }
    /// <summary>
    /// Derives a child private key using a coin type enum
    /// </summary>
    public static PrivateKey Derive(this PrivateKey privateKey, CoinType coinType, DerivationType type = DerivationType.SOFT)
    {
        int index = (int)coinType;
        return Derive(privateKey, index, type);
    }
    /// <summary>
    /// Derives a child private key using a role type enum
    /// </summary>
    public static PrivateKey Derive(this PrivateKey privateKey, RoleType roleType, DerivationType type = DerivationType.SOFT)
    {
        int index = (int)roleType;
        return Derive(privateKey, index, type);
    }
}
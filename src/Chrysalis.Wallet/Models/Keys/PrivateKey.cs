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
        ArgumentNullException.ThrowIfNull(privateKey);

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
        byte[] kl = new byte[32];
        Buffer.BlockCopy(privateKey.Key, 0, kl, 0, 32);
        byte[] kr = new byte[32];
        Buffer.BlockCopy(privateKey.Key, 32, kr, 0, 32);

        byte[] z = new byte[64];
        byte[] zl = new byte[32];
        byte[] zr = new byte[32];
        byte[] i = new byte[64];
        byte[] seri = Bip32Util.Le32(index);

        BinaryPrimitives.WriteUInt32BigEndian(seri, (uint)index);

        byte[] zBuffer = new byte[1 + privateKey.Key.Length + seri.Length];
        byte[] iBuffer = new byte[1 + privateKey.Key.Length + seri.Length];
        if (Bip32Util.FromIndex(index) == DerivationType.HARD)
        {
            zBuffer[0] = 0x00; //constant or enum?
            privateKey.Key.CopyTo(zBuffer, 1);
            seri.CopyTo(zBuffer, 1 + privateKey.Key.Length);

            iBuffer[0] = 0x01; //constant or enum?
            privateKey.Key.CopyTo(iBuffer, 1);
            seri.CopyTo(iBuffer, 1 + privateKey.Key.Length);
        }
        else
        {
            PublicKey pk = GetPublicKey(privateKey);
            zBuffer[0] = 0x02; //constant or enum?
            pk.Key.CopyTo(zBuffer, 1);
            seri.CopyTo(zBuffer, 1 + pk.Key.Length);

            iBuffer[0] = 0x03; //constant or enum?
            pk.Key.CopyTo(iBuffer, 1);
            seri.CopyTo(iBuffer, 1 + pk.Key.Length);
        }

        using (HMACSHA512 hmacSha512 = new(privateKey.Chaincode))
        {
            z = hmacSha512.ComputeHash([.. zBuffer]);
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

        //chaincode

        byte[] cc;
        using (HMACSHA512 hmacSha512 = new(privateKey.Chaincode))
        {
            i = hmacSha512.ComputeHash([.. iBuffer]);
            cc = i[32..];
        }

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
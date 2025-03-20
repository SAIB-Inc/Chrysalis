using System.Security.Cryptography;
using Chaos.NaCl;
using Chrysalis.Tx.Models.Enums;
using Chrysalis.Tx.Models.Keys;
using Chrysalis.Tx.Services;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Extensions;

public static class PrivateKeyExtensions
{
    const uint MinHardIndex = 0x80000000;

    public static PublicKey GetPublicKey(this PrivateKey privateKey)
    {
        byte[] sk = new byte[privateKey.Key.Length];
        Buffer.BlockCopy(privateKey.Key, 0, sk, 0, privateKey.Key.Length);
        byte[] pk = Ed25519.GetPublicKey(sk);

        BigEndianBuffer buffer = new();

        buffer.Write(pk);

        return new PublicKey(buffer.ToArray(), privateKey.Chaincode);
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
        PrivateKey newPrivateKey = privateKey.GetChildKeyDerivation(derivationIndex);

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

        BigEndianBuffer zBuffer = new();
        BigEndianBuffer iBuffer = new();
        if (Bip32Util.FromIndex(index) == DerivationType.HARD)
        {
            zBuffer.Write([0x00]); //constant or enum?
            zBuffer.Write(privateKey.Key);
            zBuffer.Write(seri);

            iBuffer.Write([0x01]); //constant or enum?
            iBuffer.Write(privateKey.Key);
            iBuffer.Write(seri);
        }
        else
        {
            PublicKey pk = privateKey.GetPublicKey();
            zBuffer.Write([0x02]); //constant or enum?
            zBuffer.Write(pk.Key);
            zBuffer.Write(seri);

            iBuffer.Write([0x03]); //constant or enum?
            iBuffer.Write(pk.Key);
            iBuffer.Write(seri);
        }

        using (HMACSHA512 hmacSha512 = new(privateKey.Chaincode))
        {
            z = hmacSha512.ComputeHash(zBuffer.ToArray());
            zl = z.Slice(0, 32);
            zr = z.Slice(32);
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
            i = hmacSha512.ComputeHash(iBuffer.ToArray());
            cc = i.Slice(32);
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
        return privateKey.Derive(index, type);
    }
    
    /// <summary>
    /// Derives a child private key using a coin type enum
    /// </summary>
    public static PrivateKey Derive(this PrivateKey privateKey, CoinType coinType, DerivationType type = DerivationType.SOFT)
    {
        int index = (int)coinType;
        return privateKey.Derive(index, type);
    }
    
    /// <summary>
    /// Derives a child private key using a role type enum
    /// </summary>
    public static PrivateKey Derive(this PrivateKey privateKey, RoleType roleType, DerivationType type = DerivationType.SOFT)
    {
        int index = (int)roleType;
        return privateKey.Derive(index, type);
    }
}
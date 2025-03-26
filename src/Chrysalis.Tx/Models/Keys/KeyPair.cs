using System.Security.Cryptography;
using Chaos.NaCl;

namespace Chrysalis.Tx.Models.Keys;

public class KeyPair(PrivateKey privateKey, PublicKey publicKey)
{
    public PrivateKey PrivateKey { get; private set; } = privateKey;
    public PublicKey PublicKey { get; private set; } = publicKey;

    public static KeyPair GenerateKeyPair()
    {
        byte[] privateKey = new byte[32];

        using RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create();
        randomNumberGenerator.GetBytes(privateKey);
        Ed25519.KeyPairFromSeed(out byte[] publicKey, out _, privateKey);

        return new KeyPair(new PrivateKey(privateKey, []), new PublicKey(publicKey, []));
    }
}
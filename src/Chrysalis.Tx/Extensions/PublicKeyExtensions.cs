using Chaos.NaCl;
using Chrysalis.Tx.Models.Keys;

namespace Chrysalis.Tx.Extensions;

public static class PublicKeyExtensions
{
    public static bool Verify(this PublicKey publicKey, byte[] message, byte[] signature)
    {
        return Ed25519.Verify(signature, message, publicKey.Key);
    }
}
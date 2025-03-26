using Chaos.NaCl;
using Chrysalis.Wallet.Models.Keys;

namespace Chrysalis.Wallet.Extensions;

public static class PublicKeyExtensions
{
    public static bool Verify(this PublicKey publicKey, byte[] message, byte[] signature)
    {
        return Ed25519.Verify(signature, message, publicKey.Key);
    }
}
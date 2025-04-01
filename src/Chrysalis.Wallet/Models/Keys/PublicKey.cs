using Chaos.NaCl;

namespace Chrysalis.Wallet.Models.Keys;

public class PublicKey(byte[] key, byte[] chaincode)
{
    public byte[] Key { get; set; } = key;
    public byte[] Chaincode { get; set; } = chaincode;

    public bool Verify(byte[] message, byte[] signature)
    {
        return Ed25519.Verify(signature, message, Key);
    }

    public string ToHex() => Convert.ToHexString(Key);

    public byte[] ToBlake2b224() => Blake2Fast.Blake2b.ComputeHash(28, Key);

    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
                return false;

        var other = (PublicKey)obj;
        return Key.SequenceEqual(other.Key) && Chaincode.SequenceEqual(other.Chaincode);
    }

    public override int GetHashCode() => HashCode.Combine(Convert.ToHexString(Key), Convert.ToHexString(Chaincode));
}
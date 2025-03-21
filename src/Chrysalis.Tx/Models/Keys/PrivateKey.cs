namespace Chrysalis.Tx.Models.Keys;

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
namespace Chrysalis.Wallet.Models.Keys;

public class PublicKey(byte[] key, byte[] chaincode)
{
    public byte[] Key { get; set; } = key;
    public byte[] Chaincode { get; set; } = chaincode;

    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
                return false;

        var other = (PublicKey)obj;
        return Key.SequenceEqual(other.Key) && Chaincode.SequenceEqual(other.Chaincode);
    }

    public override int GetHashCode() => HashCode.Combine(Convert.ToHexString(Key), Convert.ToHexString(Chaincode));
}
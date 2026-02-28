using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;

namespace Chrysalis.Wallet.Test.Keys;

public class PublicKeyTests
{
    private static PublicKey CreateTestPublicKey()
    {
        Mnemonic mnemonic = Mnemonic.Restore(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            English.Words);
        PrivateKey rootKey = mnemonic.GetRootKey();
        return rootKey.GetPublicKey();
    }

    [Fact]
    public void GetPublicKey_Returns32ByteKey()
    {
        PublicKey publicKey = CreateTestPublicKey();

        Assert.Equal(32, publicKey.Key.Length);
    }

    [Fact]
    public void GetPublicKey_Returns32ByteChaincode()
    {
        PublicKey publicKey = CreateTestPublicKey();

        Assert.Equal(32, publicKey.Chaincode.Length);
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        Mnemonic mnemonic = Mnemonic.Restore(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            English.Words);
        PrivateKey privateKey = mnemonic.GetRootKey();
        PublicKey publicKey = privateKey.GetPublicKey();

        byte[] message = "Hello, Cardano!"u8.ToArray();
        byte[] signature = privateKey.Sign(message);

        Assert.True(publicKey.Verify(message, signature));
    }

    [Fact]
    public void Verify_InvalidSignature_ReturnsFalse()
    {
        PublicKey publicKey = CreateTestPublicKey();
        byte[] message = "Hello, Cardano!"u8.ToArray();
        byte[] invalidSignature = new byte[64];

        Assert.False(publicKey.Verify(message, invalidSignature));
    }

    [Fact]
    public void Verify_WrongMessage_ReturnsFalse()
    {
        Mnemonic mnemonic = Mnemonic.Restore(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            English.Words);
        PrivateKey privateKey = mnemonic.GetRootKey();
        PublicKey publicKey = privateKey.GetPublicKey();

        byte[] message = "Hello, Cardano!"u8.ToArray();
        byte[] wrongMessage = "Wrong message"u8.ToArray();
        byte[] signature = privateKey.Sign(message);

        Assert.False(publicKey.Verify(wrongMessage, signature));
    }

    [Fact]
    public void ToHex_ReturnsValidHexString()
    {
        PublicKey publicKey = CreateTestPublicKey();

        string hex = publicKey.ToHex();

        Assert.Equal(64, hex.Length);
        Assert.True(hex.All(c => "0123456789ABCDEF".Contains(c, StringComparison.Ordinal)));
    }

    [Fact]
    public void ToBlake2b224_Returns28Bytes()
    {
        PublicKey publicKey = CreateTestPublicKey();

        byte[] hash = publicKey.ToBlake2b224();

        Assert.Equal(28, hash.Length);
    }

    [Fact]
    public void ToBlake2b224_SameKey_ReturnsSameHash()
    {
        PublicKey publicKey1 = CreateTestPublicKey();
        PublicKey publicKey2 = CreateTestPublicKey();

        Assert.Equal(publicKey1.ToBlake2b224(), publicKey2.ToBlake2b224());
    }

    [Fact]
    public void Equals_SamePublicKey_ReturnsTrue()
    {
        PublicKey publicKey1 = CreateTestPublicKey();
        PublicKey publicKey2 = CreateTestPublicKey();

        Assert.True(publicKey1.Equals(publicKey2));
    }

    [Fact]
    public void Equals_DifferentPublicKey_ReturnsFalse()
    {
        Mnemonic mnemonic1 = Mnemonic.Generate(English.Words, 12);
        Mnemonic mnemonic2 = Mnemonic.Generate(English.Words, 12);

        PrivateKey privateKey1 = mnemonic1.GetRootKey();
        PrivateKey privateKey2 = mnemonic2.GetRootKey();

        PublicKey publicKey1 = privateKey1.GetPublicKey();
        PublicKey publicKey2 = privateKey2.GetPublicKey();

        Assert.False(publicKey1.Equals(publicKey2));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        PublicKey publicKey = CreateTestPublicKey();

        Assert.False(publicKey.Equals(null));
    }

    [Fact]
    public void GetHashCode_SameKey_ReturnsSameHash()
    {
        PublicKey publicKey1 = CreateTestPublicKey();
        PublicKey publicKey2 = CreateTestPublicKey();

        Assert.Equal(publicKey1.GetHashCode(), publicKey2.GetHashCode());
    }
}

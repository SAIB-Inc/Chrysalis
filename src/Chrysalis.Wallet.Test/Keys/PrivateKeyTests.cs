using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;
using Xunit;

namespace Chrysalis.Wallet.Test.Keys;

public class PrivateKeyTests
{
    private static PrivateKey CreateTestRootKey()
    {
        Mnemonic mnemonic = Mnemonic.Restore(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            English.Words);
        return mnemonic.GetRootKey();
    }

    [Fact]
    public void GetRootKey_ProducesValidKey()
    {
        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);
        PrivateKey rootKey = mnemonic.GetRootKey();

        Assert.Equal(64, rootKey.Key.Length);
    }

    [Fact]
    public void GetRootKey_WithPassword_ProducesDifferentKey()
    {
        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 24);

        PrivateKey keyWithoutPassword = mnemonic.GetRootKey();
        PrivateKey keyWithPassword = mnemonic.GetRootKey("test-password");

        Assert.False(keyWithoutPassword.Key.SequenceEqual(keyWithPassword.Key));
    }

    [Fact]
    public void GetRootKey_SameMnemonic_ProducesSameKey()
    {
        string phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

        Mnemonic mnemonic1 = Mnemonic.Restore(phrase, English.Words);
        Mnemonic mnemonic2 = Mnemonic.Restore(phrase, English.Words);

        PrivateKey key1 = mnemonic1.GetRootKey();
        PrivateKey key2 = mnemonic2.GetRootKey();

        Assert.True(key1.Key.SequenceEqual(key2.Key));
        Assert.True(key1.Chaincode.SequenceEqual(key2.Chaincode));
    }

    [Fact]
    public void Derive_SoftDerivation_ProducesValidChildKey()
    {
        PrivateKey rootKey = CreateTestRootKey();
        PrivateKey childKey = rootKey.Derive(0, DerivationType.SOFT);

        Assert.Equal(64, childKey.Key.Length);
        Assert.False(rootKey.Key.SequenceEqual(childKey.Key));
    }

    [Fact]
    public void Derive_HardDerivation_ProducesValidChildKey()
    {
        PrivateKey rootKey = CreateTestRootKey();
        PrivateKey childKey = rootKey.Derive(0, DerivationType.HARD);

        Assert.Equal(64, childKey.Key.Length);
        Assert.False(rootKey.Key.SequenceEqual(childKey.Key));
    }

    [Fact]
    public void Derive_SameIndex_ProducesSameChildKey()
    {
        PrivateKey rootKey1 = CreateTestRootKey();
        PrivateKey rootKey2 = CreateTestRootKey();

        PrivateKey child1 = rootKey1.Derive(0, DerivationType.HARD);
        PrivateKey child2 = rootKey2.Derive(0, DerivationType.HARD);

        Assert.True(child1.Key.SequenceEqual(child2.Key));
    }

    [Fact]
    public void Derive_DifferentIndex_ProducesDifferentKey()
    {
        PrivateKey rootKey = CreateTestRootKey();

        PrivateKey child0 = rootKey.Derive(0, DerivationType.HARD);
        PrivateKey child1 = rootKey.Derive(1, DerivationType.HARD);

        Assert.False(child0.Key.SequenceEqual(child1.Key));
    }

    [Fact]
    public void Derive_CardanoPath_ProducesExpectedStructure()
    {
        PrivateKey rootKey = CreateTestRootKey();

        // Derive m/1852'/1815'/0'/0/0 (typical Cardano payment key path)
        PrivateKey purposeKey = rootKey.Derive(PurposeType.Shelley, DerivationType.HARD);
        PrivateKey coinKey = purposeKey.Derive(CoinType.Ada, DerivationType.HARD);
        PrivateKey accountKey = coinKey.Derive(0, DerivationType.HARD);
        PrivateKey roleKey = accountKey.Derive(RoleType.ExternalChain);
        PrivateKey addressKey = roleKey.Derive(0);

        Assert.Equal(64, addressKey.Key.Length);
    }

    [Fact]
    public void GetPublicKey_ReturnsValidPublicKey()
    {
        PrivateKey rootKey = CreateTestRootKey();
        PublicKey publicKey = rootKey.GetPublicKey();

        Assert.NotNull(publicKey);
        Assert.Equal(32, publicKey.Key.Length);
    }

    [Fact]
    public void GetPublicKey_SamePrivateKey_ProducesSamePublicKey()
    {
        PrivateKey rootKey1 = CreateTestRootKey();
        PrivateKey rootKey2 = CreateTestRootKey();

        PublicKey publicKey1 = rootKey1.GetPublicKey();
        PublicKey publicKey2 = rootKey2.GetPublicKey();

        Assert.Equal(publicKey1.Key, publicKey2.Key);
    }

    [Fact]
    public void Sign_ProducesValidSignature()
    {
        PrivateKey rootKey = CreateTestRootKey();
        byte[] message = "Hello, Cardano!"u8.ToArray();

        byte[] signature = rootKey.Sign(message);

        Assert.Equal(64, signature.Length);
    }

    [Fact]
    public void Sign_SameMessage_ProducesSameSignature()
    {
        PrivateKey rootKey1 = CreateTestRootKey();
        PrivateKey rootKey2 = CreateTestRootKey();
        byte[] message = "Hello, Cardano!"u8.ToArray();

        byte[] signature1 = rootKey1.Sign(message);
        byte[] signature2 = rootKey2.Sign(message);

        Assert.Equal(signature1, signature2);
    }

    [Fact]
    public void Sign_DifferentMessage_ProducesDifferentSignature()
    {
        PrivateKey rootKey = CreateTestRootKey();
        byte[] message1 = "Hello, Cardano!"u8.ToArray();
        byte[] message2 = "Goodbye, Cardano!"u8.ToArray();

        byte[] signature1 = rootKey.Sign(message1);
        byte[] signature2 = rootKey.Sign(message2);

        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void Equals_SameKey_ReturnsTrue()
    {
        PrivateKey key1 = CreateTestRootKey();
        PrivateKey key2 = CreateTestRootKey();

        Assert.True(key1.Equals(key2));
    }

    [Fact]
    public void Equals_DifferentKey_ReturnsFalse()
    {
        Mnemonic mnemonic1 = Mnemonic.Generate(English.Words, 12);
        Mnemonic mnemonic2 = Mnemonic.Generate(English.Words, 12);

        PrivateKey key1 = mnemonic1.GetRootKey();
        PrivateKey key2 = mnemonic2.GetRootKey();

        Assert.False(key1.Equals(key2));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        PrivateKey key = CreateTestRootKey();

        Assert.False(key.Equals(null));
    }

    [Fact]
    public void GetHashCode_SameKey_ReturnsSameHash()
    {
        PrivateKey key1 = CreateTestRootKey();
        PrivateKey key2 = CreateTestRootKey();

        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
    }
}

using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;
using Xunit;

namespace Chrysalis.Wallet.Test.Keys;

public class MnemonicTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(18)]
    [InlineData(21)]
    [InlineData(24)]
    public void Generate_ValidWordLength_ReturnsCorrectWordCount(int wordLength)
    {
        Mnemonic mnemonic = Mnemonic.Generate(English.Words, wordLength);
        Assert.Equal(wordLength, mnemonic.Words.Length);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(25)]
    public void Generate_InvalidWordLength_ThrowsArgumentOutOfRangeException(int wordLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Mnemonic.Generate(English.Words, wordLength));
    }

    [Fact]
    public void Generate_InvalidWordList_ThrowsArgumentOutOfRangeException()
    {
        string[] invalidWordList = new string[100];
        Assert.Throws<ArgumentOutOfRangeException>(() => Mnemonic.Generate(invalidWordList, 12));
    }

    [Fact]
    public void Restore_ValidMnemonic_RestoresCorrectly()
    {
        string phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

        Mnemonic mnemonic = Mnemonic.Restore(phrase, English.Words);

        Assert.Equal(12, mnemonic.Words.Length);
        Assert.Equal(phrase, string.Join(" ", mnemonic.Words));
    }

    [Fact]
    public void Restore_24WordMnemonic_RestoresCorrectly()
    {
        string phrase = "scout always message drill gorilla laptop electric decrease fly actor tuition merit clock flush end duck dance treat idle replace bulk total tool assist";

        Mnemonic mnemonic = Mnemonic.Restore(phrase, English.Words);

        Assert.Equal(24, mnemonic.Words.Length);
        Assert.Equal(phrase, string.Join(" ", mnemonic.Words));
    }

    [Fact]
    public void Restore_InvalidWord_ThrowsArgumentException()
    {
        string invalidPhrase = "invalid word here abandon abandon abandon abandon abandon abandon abandon abandon about";

        Assert.Throws<ArgumentException>(() => Mnemonic.Restore(invalidPhrase, English.Words));
    }

    [Fact]
    public void Restore_InvalidChecksum_ThrowsFormatException()
    {
        // Valid words but invalid checksum
        string invalidChecksum = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon";

        Assert.Throws<FormatException>(() => Mnemonic.Restore(invalidChecksum, English.Words));
    }

    [Fact]
    public void GetWords_ReturnsCorrectWords()
    {
        string phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
        Mnemonic mnemonic = Mnemonic.Restore(phrase, English.Words);

        string[] words = mnemonic.Words;

        Assert.Equal(12, words.Length);
        Assert.All(words.Take(11), w => Assert.Equal("abandon", w));
        Assert.Equal("about", words[11]);
    }

    [Fact]
    public void GetEntropy_ReturnsExpectedLength()
    {
        Mnemonic mnemonic = Mnemonic.Generate(English.Words, 12);

        Assert.Equal(16, mnemonic.Entropy.Length);
    }

    [Fact]
    public void GenerateAndRestore_RoundTrip_ProducesSameEntropy()
    {
        Mnemonic original = Mnemonic.Generate(English.Words, 24);
        string phrase = string.Join(" ", original.Words);
        byte[] originalEntropy = original.Entropy;

        Mnemonic restored = Mnemonic.Restore(phrase, English.Words);
        byte[] restoredEntropy = restored.Entropy;

        Assert.Equal(originalEntropy, restoredEntropy);
    }

    [Fact]
    public void Restore_9WordMnemonic_ThrowsFormatException()
    {
        // 9-word mnemonics are non-standard and should be rejected
        Assert.Throws<FormatException>(() =>
            Mnemonic.Restore("abandon abandon abandon abandon abandon abandon abandon abandon about", English.Words));
    }
}

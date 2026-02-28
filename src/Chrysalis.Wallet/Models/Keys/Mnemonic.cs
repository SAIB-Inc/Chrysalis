using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace Chrysalis.Wallet.Models.Keys;

/// <summary>
/// Represents a BIP-39 mnemonic phrase for generating deterministic wallets.
/// </summary>
public record Mnemonic
{
    #region Properties

    /// <summary>
    /// Gets the array of mnemonic words.
    /// </summary>
    public string[] Words { get; private set; }

    /// <summary>
    /// Gets the entropy bytes from which the mnemonic was derived.
    /// </summary>
    public byte[] Entropy { get; private set; }

    #endregion

    #region Constants and Fields

    private static readonly int[] AllowedEntropyLengths = [16, 20, 24, 28, 32];
    private static readonly int[] AllowedWordLengths = [12, 15, 18, 21, 24];
    private const int AllWordsLength = 2048; // 1111 1111 111 -> 0..2047
    private const int BitsPerWord = 11;

    #endregion

    #region Constructors

    private Mnemonic(string[] words, byte[] entropy)
    {
        Words = words;
        Entropy = entropy;
    }

    #endregion

    #region Mnemonic Generation and Restoration

    /// <summary>
    /// Generates a new mnemonic from a given word list and word length.
    /// </summary>
    /// <param name="wordLists">The BIP-39 word list containing 2048 words.</param>
    /// <param name="wordLength">The desired mnemonic length (12, 15, 18, 21, or 24).</param>
    /// <returns>A new Mnemonic instance.</returns>
    public static Mnemonic Generate(string[] wordLists, int wordLength = 12)
    {
        ArgumentNullException.ThrowIfNull(wordLists);

        if (wordLists.Length is not AllWordsLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wordLists),
                $"Expected {nameof(wordLists)} length of {AllWordsLength}, but got {wordLists.Length}."
            );
        }

        if (!AllowedWordLengths.Contains(wordLength))
        {
            throw new ArgumentOutOfRangeException(
                nameof(wordLength),
                $"Invalid {nameof(wordLength)}: {wordLength}. Must be one of ({string.Join(", ", AllowedWordLengths)})."
            );
        }

        int entropySize = AllowedEntropyLengths[Array.FindIndex(AllowedWordLengths, x => x == wordLength)];
        if (!AllowedEntropyLengths.Contains(entropySize))
        {
            throw new ArgumentOutOfRangeException(
                nameof(wordLength),
                $"Invalid derived entropy size: {entropySize}. Must be one of ({string.Join(", ", AllowedEntropyLengths)})."
            );
        }

        byte[] entropy = new byte[entropySize];
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(entropy);

        return CreateMnemonicFromEntropy(entropy, wordLists);
    }

    /// <summary>
    /// Restores a mnemonic from a space-separated string.
    /// </summary>
    /// <param name="mnemonic">The space-separated mnemonic phrase.</param>
    /// <param name="wordLists">The BIP-39 word list containing 2048 words.</param>
    /// <returns>A restored Mnemonic instance.</returns>
    public static Mnemonic Restore(string mnemonic, string[] wordLists)
    {
        ArgumentNullException.ThrowIfNull(mnemonic);
        ArgumentNullException.ThrowIfNull(wordLists);

        string[] wordArr = mnemonic.Split(' ');
        return Restore(wordArr, wordLists);
    }

    /// <summary>
    /// Restores a mnemonic from an array of words.
    /// </summary>
    /// <param name="mnemonicWords">The array of mnemonic words.</param>
    /// <param name="wordLists">The BIP-39 word list containing 2048 words.</param>
    /// <returns>A restored Mnemonic instance.</returns>
    public static Mnemonic Restore(string[] mnemonicWords, string[] wordLists)
    {
        ArgumentNullException.ThrowIfNull(mnemonicWords);
        ArgumentNullException.ThrowIfNull(wordLists);

        // Validate wordLists has the correct number of words
        if (wordLists.Length is not AllWordsLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wordLists),
                $"Expected {nameof(wordLists)} length of {AllWordsLength}, but got {wordLists.Length}."
            );
        }

        // Validate all mnemonic words exist in wordLists
        if (!mnemonicWords.All(x => wordLists.Contains(x)))
        {
            throw new ArgumentException("Seed has invalid words.", nameof(mnemonicWords));
        }

        // Validate mnemonic length is one of the allowed lengths
        if (!AllowedWordLengths.Contains(mnemonicWords.Length))
        {
            throw new FormatException($"Invalid seed length. It must be one of the following values ({string.Join(", ", AllowedWordLengths)})");
        }

        // Calculate effective entropy length (accounting for checksum)
        int entropyBitLength = mnemonicWords.Length * BitsPerWord * 32 / 33;
        int entropyByteLength = entropyBitLength / 8;

        if (!AllowedEntropyLengths.Contains(entropyByteLength))
        {
            throw new ArgumentException($"Invalid entropy length derived from mnemonic: {entropyByteLength} bytes.", nameof(mnemonicWords));
        }

        int checksumBitLength = (mnemonicWords.Length * BitsPerWord) - entropyBitLength;
        byte[] entropy = new byte[entropyByteLength];

        // Convert each mnemonic word to its index in the word list.
        int[] indices = [.. mnemonicWords.Select(word => Array.IndexOf(wordLists, word))];

        // Recreate the entropy bits (ignoring the checksum portion)
        for (int i = 0; i < indices.Length; i++)
        {
            int wordIndex = indices[i];
            for (int j = 0; j < BitsPerWord; j++)
            {
                int bitPosition = (i * BitsPerWord) + j;
                if (bitPosition < entropyBitLength)
                {
                    bool bitValue = ((wordIndex >> (BitsPerWord - 1 - j)) & 1) == 1;
                    if (bitValue)
                    {
                        int byteIndex = bitPosition / 8;
                        int bitIndexInByte = 7 - (bitPosition % 8);
                        entropy[byteIndex] |= (byte)(1 << bitIndexInByte);
                    }
                }
            }
        }

        // Verify checksum against SHA256 hash of the entropy.
        byte[] hash = SHA256.HashData(entropy);
        for (int i = 0; i < checksumBitLength; i++)
        {
            int entropyBitPosition = entropyBitLength + i;
            int wordIndex = entropyBitPosition / BitsPerWord;
            int bitIndexInWord = entropyBitPosition % BitsPerWord;

            bool expectedBit = ((indices[wordIndex] >> (BitsPerWord - 1 - bitIndexInWord)) & 1) == 1;
            int hashBitIndex = i % 8;
            bool actualBit = ((hash[i / 8] >> (7 - hashBitIndex)) & 1) == 1;

            if (expectedBit != actualBit)
            {
                throw new FormatException("Invalid mnemonic checksum.");
            }
        }

        return new Mnemonic(mnemonicWords, entropy);
    }

    /// <summary>
    /// Creates a mnemonic from entropy by calculating the checksum and mapping bits to words.
    /// </summary>
    /// <param name="entropy">The entropy bytes.</param>
    /// <param name="wordList">The BIP-39 word list.</param>
    /// <returns>A new Mnemonic instance derived from the given entropy.</returns>
    public static Mnemonic CreateMnemonicFromEntropy(byte[] entropy, string[] wordList)
    {
        ArgumentNullException.ThrowIfNull(entropy);
        ArgumentNullException.ThrowIfNull(wordList);

        // Calculate checksum length in bits (entropy length in bits / 32)
        int checksumBitLength = entropy.Length * 8 / 32;

        // Generate SHA256 hash of the entropy (used for checksum)
        byte[] hash = SHA256.HashData(entropy);

        // Calculate total bit length (entropy bits + checksum bits)
        int totalBitLength = (entropy.Length * 8) + checksumBitLength;
        int wordCount = totalBitLength / BitsPerWord; // Each word represents 11 bits

        // Process 11 bits at a time to generate word indices
        string[] words = [.. Enumerable.Range(0, wordCount)
            .Select(i =>
            {
                int startBit = i * BitsPerWord;
                int index = GetWordIndexFromBits(entropy, hash, startBit);
                return wordList[index].Normalize(NormalizationForm.FormKD);
            })];

        return new(words, entropy);
    }

    /// <summary>
    /// Derives the root private key from a mnemonic phrase, applying Ed25519 scalar clamping to ensure key compliance.
    /// </summary>
    /// <param name="password">Optional password for additional key derivation security (default is empty string).</param>
    /// <returns>A PrivateKey instance with the derived and clamped root key.</returns>
    /// <remarks>
    /// Scalar clamping is a critical cryptographic process that ensures the generated private key
    /// is suitable for use with Ed25519 signatures. It involves three specific bit manipulations:
    ///
    /// 1. Clear the lowest 3 bits (rootKey[0] &amp;= 0b1111_1000):
    ///    - Ensures the scalar is a multiple of 8, improving performance
    ///    - Prevents small-subgroup attacks by restricting the scalar's range
    ///
    /// 2. Clear the highest 3 bits (rootKey[31] &amp;= 0b0001_1111):
    ///    - Prevents potential side-channel attacks
    ///    - Limits the scalar's magnitude to prevent overflow
    ///
    /// 3. Set the second-highest bit (rootKey[31] |= 0b0100_0000):
    ///    - Guarantees the scalar is within a specific range
    ///    - Provides additional cryptographic hardening
    /// </remarks>
    public PrivateKey GetRootKey(string password = "")
    {
        byte[] rootKey = KeyDerivation.Pbkdf2(password, Entropy, KeyDerivationPrf.HMACSHA512, 4096, 96);
        rootKey[0] &= 0b1111_1000;
        rootKey[31] &= 0b0001_1111;
        rootKey[31] |= 0b0100_0000;

        byte[] key = new byte[64];
        byte[] chaincode = new byte[32];

        Array.Copy(rootKey, 0, key, 0, 64);
        Array.Copy(rootKey, 64, chaincode, 0, 32);

        return new PrivateKey(key, chaincode);
    }

    #endregion

    #region Bit Utilities

    /// <summary>
    /// Extracts 11 bits (BIP39 standard) from the combined entropy and checksum,
    /// and returns them as an integer index for the word list.
    /// </summary>
    private static int GetWordIndexFromBits(byte[] entropy, byte[] hash, int startBit)
    {
        const int bitsPerWord = 11;
        int entropyBitLength = entropy.Length * 8;

        // Use LINQ to process the bits and calculate the index
        return Enumerable.Range(0, bitsPerWord)
            .Sum(bitOffset =>
            {
                int position = startBit + bitOffset;
                bool bitValue = position < entropyBitLength
                    ? GetBitFromByteArray(entropy, position)
                    : GetBitFromByteArray(hash, position - entropyBitLength);

                // Calculate bit contribution to the final index (MSB first)
                return bitValue ? 1 << (bitsPerWord - 1 - bitOffset) : 0;
            }); // Sum all bit values to get the final index
    }

    /// <summary>
    /// Extracts a single bit from a byte array at the specified bit position.
    /// </summary>
    private static bool GetBitFromByteArray(byte[] data, int bitPosition)
    {
        int byteIndex = bitPosition / 8;
        int bitIndex = 7 - (bitPosition % 8); // MSB first
        return ((data[byteIndex] >> bitIndex) & 1) == 1;
    }

    #endregion
}

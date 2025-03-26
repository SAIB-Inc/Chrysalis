using System.Security.Cryptography;
using System.Text;

namespace Chrysalis.Wallet.Models.Keys;

public class Mnemonic
{
    public string[] Words { get; private set; }
    public byte[] Entropy { get; private set; }

    private Mnemonic(string[] words, byte[] entropy)
    {
        Words = words;
        Entropy = entropy;
    }

    private static readonly int[] _allowedEntropyLengths = [12, 16, 20, 24, 28, 32];
    private static readonly int[] _allowedWordLengths = [9, 12, 15, 18, 21, 24];
    private const int _allWordsLength = 2048; // 1111 1111 111 -> 0..2047
    private const int _bitsPerWord = 11;

    public static Mnemonic Generate(string[] wordLists, int wordLength = 12)
    {
        if (wordLists.Length is not _allWordsLength)
            throw new ArgumentOutOfRangeException(
                nameof(wordLists.Length),
                $"Expected {nameof(wordLists)} length of {_allWordsLength}, but got {wordLists.Length}."
            );

        if (!_allowedWordLengths.Contains(wordLength))
            throw new ArgumentOutOfRangeException(
                nameof(wordLength),
                $"Invalid {nameof(wordLength)}: {wordLength}. Must be one of ({string.Join(", ", _allowedWordLengths)})."
            );

        int entropySize = _allowedEntropyLengths[Array.FindIndex(_allowedWordLengths, x => x == wordLength)];
        if (!_allowedEntropyLengths.Contains(entropySize))
            throw new ArgumentOutOfRangeException(
                nameof(entropySize),
                $"Invalid derived entropy size: {entropySize}. Must be one of ({string.Join(", ", _allowedEntropyLengths)})."
            );

        // Generate cryptographically secure random bytes for entropy
        byte[] entropy = new byte[entropySize];
        using RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create();
        randomNumberGenerator.GetBytes(entropy);

        return CreateMnemonicFromEntropy(entropy, wordLists);
    }

    public static Mnemonic Restore(string mnemonic, string[] wordLists)
    {
        string[] wordArr = mnemonic.Split(' ');
        return Restore(wordArr, wordLists);
    }

    public static Mnemonic Restore(string[] mnemonic, string[] wordLists)
    {
        // Validate wordLists has the correct number of words
        if (wordLists.Length is not _allWordsLength)
            throw new ArgumentOutOfRangeException(
                nameof(wordLists.Length),
                $"Expected {nameof(wordLists)} length of {_allWordsLength}, but got {wordLists.Length}."
            );

        // Validate all mnemonic words exist in wordLists
        if (!mnemonic.All(x => wordLists.Contains(x)))
            throw new ArgumentException(nameof(mnemonic), "Seed has invalid words.");

        // Validate mnemonic length is one of the allowed lengths
        if (!_allowedWordLengths.Contains(mnemonic.Length))
            throw new FormatException($"Invalid seed length. It must be one of the following values ({string.Join(", ", _allowedWordLengths)})");

        // Calculate derived entropy length based on the mnemonic word count
        int entropyBitLength = mnemonic.Length * _bitsPerWord * 32 / 33; // (mnemonic length * 11) * (32/33) to account for checksum
        int entropyByteLength = entropyBitLength / 8;

        // Validate the calculated entropy length
        if (!_allowedEntropyLengths.Contains(entropyByteLength))
            throw new ArgumentException($"Invalid entropy length derived from mnemonic: {entropyByteLength} bytes.");

        // Calculate checksum bit length
        int checksumBitLength = mnemonic.Length * _bitsPerWord - entropyBitLength;

        // Initialize entropy bytes array
        byte[] entropy = new byte[entropyByteLength];

        // Convert mnemonic words to indices in the wordLists
        int[] indices = [.. mnemonic.Select(word => Array.IndexOf(wordLists, word))];

        // Recreate the combined entropy and checksum bits
        for (int i = 0; i < indices.Length; i++)
        {
            int wordIndex = indices[i];

            // For each bit in the current word index
            for (int j = 0; j < _bitsPerWord; j++)
            {
                // Calculate the overall bit position
                int bitPosition = i * _bitsPerWord + j;

                // Extract the bit from the word index (MSB first)
                bool bitValue = ((wordIndex >> (_bitsPerWord - 1 - j)) & 1) == 1;

                // If this is an entropy bit (not a checksum bit), set it in the entropy array
                if (bitPosition < entropyBitLength)
                {
                    int byteIndex = bitPosition / 8;
                    int bitIndexInByte = 7 - (bitPosition % 8); // MSB first

                    if (bitValue)
                    {
                        entropy[byteIndex] |= (byte)(1 << bitIndexInByte);
                    }
                }
            }
        }

        // Verify the checksum
        byte[] hash = SHA256.HashData(entropy);

        // Verify each checksum bit
        for (int i = 0; i < checksumBitLength; i++)
        {
            int entropyBitPosition = entropyBitLength + i;
            int wordIndex = entropyBitPosition / _bitsPerWord;
            int bitIndexInWord = entropyBitPosition % _bitsPerWord;

            // Get the expected checksum bit from the mnemonic
            bool expectedBit = ((indices[wordIndex] >> (_bitsPerWord - 1 - bitIndexInWord)) & 1) == 1;

            // Get the actual checksum bit from the hash
            int hashBitIndex = i % 8;
            bool actualBit = ((hash[i / 8] >> (7 - hashBitIndex)) & 1) == 1;

            // If checksums don't match, throw an exception
            if (expectedBit != actualBit)
                throw new FormatException("Invalid mnemonic checksum.");
        }

        // Return the restored MnemonicKey with the original words and derived entropy
        return new Mnemonic(mnemonic, entropy);
    }

    // This should be private
    public static Mnemonic CreateMnemonicFromEntropy(byte[] entropy, string[] wordList)
    {
        // Calculate checksum length in bits (entropy length in bits / 32)
        int checksumBitLength = entropy.Length * 8 / 32;

        // Generate SHA256 hash of the entropy (used for checksum)
        byte[] hash = SHA256.HashData(entropy);

        // Calculate total bit length (entropy bits + checksum bits)
        int totalBitLength = entropy.Length * 8 + checksumBitLength;
        int wordCount = totalBitLength / _bitsPerWord; // Each word represents 11 bits

        // Process 11 bits at a time to generate word indices
        string[] words = [.. Enumerable.Range(0, wordCount)
            .Select(i =>
            {
                int startBit = i * _bitsPerWord;
                int index = GetWordIndexFromBits(entropy, hash, startBit);
                return wordList[index].Normalize(NormalizationForm.FormKD);
            })];

        return new(words, entropy);
    }

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
            .Select(bitOffset =>
            {
                int position = startBit + bitOffset;
                bool bitValue = position < entropyBitLength
                    ? GetBitFromByteArray(entropy, position)
                    : GetBitFromByteArray(hash, position - entropyBitLength);

                // Calculate bit contribution to the final index (MSB first)
                return bitValue ? 1 << (bitsPerWord - 1 - bitOffset) : 0;
            })
            .Sum(); // Sum all bit values to get the final index
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
};
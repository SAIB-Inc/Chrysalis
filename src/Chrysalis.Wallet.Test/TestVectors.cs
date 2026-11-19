namespace Chrysalis.Wallet.Test;

internal static class TestVectors
{
    /// <summary>
    /// BIP-39 test vector, English, entropy 00×16, from trezor/python-mnemonic <c>vectors.json</c>,
    /// which BIP-39 names as its test vectors (bips <c>b9f9a8d6</c>, <c>bip-0039.mediawiki:130-135</c>).
    /// </summary>
    internal const string Bip39ZeroEntropy12 = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
}

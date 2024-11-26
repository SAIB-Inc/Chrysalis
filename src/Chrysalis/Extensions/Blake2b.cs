using NSec.Cryptography;

namespace Chrysalis.Extensions;
public static class Blake2bExtension
{
    public static byte[] ToBlake2b(this byte[] input)
    {
        Blake2b algorithm = HashAlgorithm.Blake2b_256;
        return algorithm.Hash(input);
    }
}

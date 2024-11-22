using NSec.Cryptography;

namespace Chrysalis.Utils;
public static class Blake2bUtils
{
    public static byte[] ToBlake2b(this byte[] input)
    {
        Blake2b algorithm = HashAlgorithm.Blake2b_256;
        return algorithm.Hash(input);
    }
}

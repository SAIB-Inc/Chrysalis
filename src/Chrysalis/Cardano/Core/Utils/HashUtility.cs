using NSec.Cryptography;

namespace Chrysalis.Cardano.Core.Utils;

public static class HashUtility
{
    public static byte[] Blake2b256(this byte[] input)
    {
        Blake2b algorithm = HashAlgorithm.Blake2b_256;
        return algorithm.Hash(input);
    }
}
namespace Chrysalis.Wallet.Extensions;

public static class ByteArrayExtension
{
    /// <summary>
    /// Concatinates two given byte arrays and returns a new byte array containing all the elements.
    /// </summary>
    /// <remarks>
    /// This is a lot faster than Linq (~30 times)
    /// </remarks>
    /// <exception cref="ArgumentNullException"/>
    /// <param name="firstArray">First set of bytes in the final array.</param>
    /// <param name="secondArray">Second set of bytes in the final array.</param>
    /// <returns>The concatinated array of bytes.</returns>
    public static byte[] ConcatFast(this byte[] firstArray, byte[] secondArray)
    {
        if (firstArray == null)
            throw new ArgumentNullException(nameof(firstArray), "First array can not be null!");
        if (secondArray == null)
            throw new ArgumentNullException(nameof(secondArray), "Second array can not be null!");

        byte[] result = new byte[firstArray.Length + secondArray.Length];
        Buffer.BlockCopy(firstArray, 0, result, 0, firstArray.Length);
        Buffer.BlockCopy(secondArray, 0, result, firstArray.Length, secondArray.Length);
        return result;
    }
}
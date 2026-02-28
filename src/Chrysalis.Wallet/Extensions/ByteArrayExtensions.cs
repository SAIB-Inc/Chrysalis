namespace Chrysalis.Wallet.Extensions;

/// <summary>
/// Extension methods for byte array operations.
/// </summary>
public static class ByteArrayExtension
{
    /// <summary>
    /// Concatenates two given byte arrays and returns a new byte array containing all the elements.
    /// </summary>
    /// <remarks>
    /// This is a lot faster than Linq (~30 times)
    /// </remarks>
    /// <exception cref="ArgumentNullException"/>
    /// <param name="firstArray">First set of bytes in the final array.</param>
    /// <param name="secondArray">Second set of bytes in the final array.</param>
    /// <returns>The concatenated array of bytes.</returns>
    public static byte[] ConcatFast(this byte[] firstArray, byte[] secondArray)
    {
        ArgumentNullException.ThrowIfNull(firstArray);
        ArgumentNullException.ThrowIfNull(secondArray);

        byte[] result = new byte[firstArray.Length + secondArray.Length];
        Buffer.BlockCopy(firstArray, 0, result, 0, firstArray.Length);
        Buffer.BlockCopy(secondArray, 0, result, firstArray.Length, secondArray.Length);
        return result;
    }
}

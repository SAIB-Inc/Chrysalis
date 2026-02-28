namespace Chrysalis.Wallet.Utils;

/// <summary>
/// Utility class for byte array manipulation operations.
/// </summary>
public static class ByteArrayUtil
{
    /// <summary>
    /// Converts an integer to a big-endian byte array of the specified size.
    /// </summary>
    /// <param name="value">The integer value to convert.</param>
    /// <param name="size">The desired byte array size.</param>
    /// <returns>A big-endian byte array representation of the value.</returns>
    public static byte[] FromIntBigEndian(int value, int size)
    {
        int minBytes = value == 0 ? 1 : (int)Math.Ceiling(Math.Log(Math.Abs((double)value) + 1, 256));

        if (minBytes > size)
        {
            throw new ArgumentException($"Value {value} cannot fit in {size} bytes", nameof(value));
        }

        byte[] valueBytes = BitConverter.GetBytes(value);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(valueBytes);
        }

        byte[] result = new byte[size];

        int sourceStartIndex = Math.Max(0, valueBytes.Length - size);
        int destinationStartIndex = Math.Max(0, size - valueBytes.Length);
        int copyLength = Math.Min(valueBytes.Length, size);

        Array.Copy(valueBytes, sourceStartIndex, result, destinationStartIndex, copyLength);

        return result;
    }

    /// <summary>
    /// Concatenates multiple byte arrays into a single byte array.
    /// </summary>
    /// <param name="arrays">The byte arrays to concatenate.</param>
    /// <returns>A single byte array containing all input arrays.</returns>
    public static byte[] ConcatByteArrays(params byte[][] arrays)
    {
        ArgumentNullException.ThrowIfNull(arrays);

        int totalLength = arrays.Sum(a => a.Length);
        byte[] result = new byte[totalLength];

        int offset = 0;
        foreach (byte[] array in arrays)
        {
            Buffer.BlockCopy(array, 0, result, offset, array.Length);
            offset += array.Length;
        }

        return result;
    }
}

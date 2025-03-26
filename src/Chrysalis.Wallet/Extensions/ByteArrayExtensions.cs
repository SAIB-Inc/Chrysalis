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

    /// <summary>
    /// Creates a new array from the given array by taking a specified number of items starting from a given index.
    /// </summary>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="IndexOutOfRangeException"/>
    /// <param name="sourceArray">The array containing bytes to take.</param>
    /// <param name="index">Starting index in <paramref name="sourceArray"/>.</param>
    /// <param name="count">Number of elements to take.</param>
    /// <returns>An array of bytes.</returns>
    public static byte[] SubArray(this byte[] sourceArray, int index, int count)
    {
        if (sourceArray == null)
            throw new ArgumentNullException(nameof(sourceArray), "Input can not be null!");
        if (index < 0 || count < 0)
            throw new IndexOutOfRangeException("Index or count can not be negative.");
        if (sourceArray.Length != 0 && index > sourceArray.Length - 1 || sourceArray.Length == 0 && index != 0)
            throw new IndexOutOfRangeException("Index can not be bigger than array length.");
        if (count > sourceArray.Length - index)
            throw new IndexOutOfRangeException("Array is not long enough.");

        byte[] result = new byte[count];
        Buffer.BlockCopy(sourceArray, index, result, 0, count);
        return result;
    }

    /// <summary>
    /// Creates a new array from the given array by taking items starting from a given index.
    /// </summary>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="IndexOutOfRangeException"/>
    /// <param name="sourceArray">The array containing bytes to take.</param>
    /// <param name="index">Starting index in <paramref name="sourceArray"/>.</param>
    /// <returns>An array of bytes.</returns>
    public static byte[] SubArray(this byte[] sourceArray, int index)
    {
        if (sourceArray == null)
            throw new ArgumentNullException(nameof(sourceArray), "Input can not be null!");
        if (sourceArray.Length != 0 && index > sourceArray.Length - 1 || sourceArray.Length == 0 && index != 0)
            throw new IndexOutOfRangeException("Index can not be bigger than array length.");

        return SubArray(sourceArray, index, sourceArray.Length - index);
    }

    public static byte[] Prepend(this byte[] bytes, byte[] bytesToPrepend)
    {
        byte[] tmp = new byte[bytes.Length + bytesToPrepend.Length];
        bytesToPrepend.CopyTo(tmp, 0);
        bytes.CopyTo(tmp, bytesToPrepend.Length);
        return tmp;
    }

    public static (byte[] left, byte[] right) Shift(this byte[] bytes, int size)
    {
        byte[] left = new byte[size];
        byte[] right = new byte[bytes.Length - size];

        Array.Copy(bytes, 0, left, 0, left.Length);
        Array.Copy(bytes, left.Length, right, 0, right.Length);

        return (left, right);
    }

    /// <summary>
    /// Returns the last n bits of the byte
    /// </summary>
    /// <param name="b"></param>
    /// <param name="n"></param>
    /// <returns></returns>
    public static int LastBits(this byte b, int n)
    {
        if (n > 8)
        {
            throw new InvalidOperationException($"{nameof(n)} must be <= 8");
        }

        int mask = ~(0xff >> n << n);
        return b & mask;
    }

     public static T[] Slice<T>(this T[] source, int start, int end)
    {
        if (end < 0)
            end = source.Length;

        int len = end - start;

        // Return new array.
        T[] res = new T[len];
        for (int i = 0; i < len; i++)
            res[i] = source[i + start];
        return res;
    }

    public static T[] Slice<T>(this T[] source, int start)
    {
        return Slice<T>(source, start, -1);
    }
}
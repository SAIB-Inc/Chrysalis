namespace Chrysalis.Wallet.Utils;

public static class ByteArrayUtil
{
    public static byte[] FromIntBigEndian(int value, int size)
    {
        int minBytes = value == 0 ? 1 : (int)Math.Ceiling(Math.Log(Math.Abs((double)value) + 1, 256));

        if (minBytes > size)
        {
            throw new ArgumentException($"Value {value} cannot fit in {size} bytes");
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

    public static byte[] ConcatByteArrays(params byte[][] arrays)
    {
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
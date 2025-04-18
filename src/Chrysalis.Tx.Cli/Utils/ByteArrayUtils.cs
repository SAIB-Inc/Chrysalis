namespace Chrysalis.Tx.Cli.Utils;

public static class ByteArrayUtils
{
    public static byte[] FromIntBigEndian(int value, int size)
    {
        // Ensure the value fits within the desired size (1 byte for value '2' is sufficient)
        byte[] byteArray = BitConverter.GetBytes(value);

        // Reverse byte array if the system is Little-Endian
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(byteArray);
        }

        // Check if the byteArray fits the size
        if (byteArray.Length > size)
        {
            byte[] truncatedArray = new byte[size];
            Array.Copy(byteArray, 0, truncatedArray, 0, size);
            return truncatedArray;
        }

        // If padding is necessary (only when byteArray is smaller than the size), pad with leading zeros
        if (byteArray.Length < size)
        {
            byte[] paddedArray = new byte[size];
            Array.Copy(byteArray, 0, paddedArray, size - byteArray.Length, byteArray.Length);
            return paddedArray;
        }

        return byteArray;
    }
}

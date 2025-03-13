namespace PlutusVM.Eval.Utils;

internal static class Converter
{
    public static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return [];

        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];

        if (hex.Length % 2 != 0)
            throw new FormatException("Hexadecimal string must have an even number of characters");

        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            string byteValue = hex.Substring(i * 2, 2);
            try
            {
                bytes[i] = Convert.ToByte(byteValue, 16);
            }
            catch (Exception ex)
            {
                throw new FormatException($"Invalid hexadecimal character '{byteValue}' at position {i * 2}", ex);
            }
        }

        return bytes;
    }
    public static string BytesToHex(byte[]? bytes, bool prefix = false)
    {
        if (bytes is null or { Length: 0 })
            return prefix ? "0x" : string.Empty;

        var hex = Convert.ToHexString(bytes);
        return prefix ? $"0x{hex}" : hex;
    }
}
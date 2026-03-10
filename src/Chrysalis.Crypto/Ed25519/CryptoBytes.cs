using System.Runtime.CompilerServices;

namespace Chrysalis.Crypto;

/// <summary>
/// Provides constant-time byte comparison and secure memory wiping operations for cryptographic use.
/// </summary>
internal static class CryptoBytes
{
    /// <summary>
    /// Compares two byte arrays in constant time to prevent timing side-channel attacks.
    /// </summary>
    /// <param name="x">The first byte array.</param>
    /// <param name="xOffset">The offset into the first array.</param>
    /// <param name="y">The second byte array.</param>
    /// <param name="yOffset">The offset into the second array.</param>
    /// <param name="length">The number of bytes to compare.</param>
    /// <returns><see langword="true"/> if the byte ranges are equal; otherwise <see langword="false"/>.</returns>
    internal static bool ConstantTimeEquals(byte[] x, int xOffset, byte[] y, int yOffset, int length)
    {
        int differentbits = 0;
        for (int i = 0; i < length; i++)
            differentbits |= x[xOffset + i] ^ y[yOffset + i];
        return (1 & (unchecked((uint)differentbits - 1) >> 8)) != 0;
    }

    /// <summary>
    /// Securely wipes a byte array by setting all elements to zero.
    /// </summary>
    /// <param name="data">The byte array to wipe.</param>
    internal static void Wipe(byte[] data)
    {
        InternalWipe(data, 0, data.Length);
    }

    /// <summary>
    /// Securely wipes a byte array by setting all elements in the specified range to zero.
    /// The <see cref="MethodImplOptions.NoInlining"/> attribute prevents the compiler from
    /// optimizing away the wipe operation.
    /// </summary>
    /// <param name="data">The byte array to wipe.</param>
    /// <param name="offset">The starting offset.</param>
    /// <param name="count">The number of bytes to wipe.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void InternalWipe(byte[] data, int offset, int count)
    {
        Array.Clear(data, offset, count);
    }

    /// <summary>
    /// Securely wipes a struct value by resetting it to its default state.
    /// </summary>
    /// <typeparam name="T">The struct type.</typeparam>
    /// <param name="data">A reference to the struct to wipe.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void InternalWipe<T>(ref T data)
        where T : struct
    {
        data = default;
    }
}

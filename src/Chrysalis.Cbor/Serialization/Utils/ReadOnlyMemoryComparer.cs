namespace Chrysalis.Cbor.Serialization.Utils;

/// <summary>
/// Provides equality comparison for <see cref="ReadOnlyMemory{T}"/> of bytes using sequence equality.
/// </summary>
public sealed class ReadOnlyMemoryComparer : IEqualityComparer<ReadOnlyMemory<byte>>
{
    /// <summary>
    /// Gets the singleton instance of the comparer.
    /// </summary>
    public static readonly ReadOnlyMemoryComparer Instance = new();

    private ReadOnlyMemoryComparer() { }

    /// <summary>
    /// Determines whether two memory regions are equal by comparing their byte sequences.
    /// </summary>
    /// <param name="x">The first memory region.</param>
    /// <param name="y">The second memory region.</param>
    /// <returns>True if the regions are sequence-equal; otherwise, false.</returns>
    public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y)
    {
        return x.Span.SequenceEqual(y.Span);
    }

    /// <summary>
    /// Returns a hash code for the specified memory region.
    /// </summary>
    /// <param name="obj">The memory region to hash.</param>
    /// <returns>A hash code for the memory region.</returns>
    public int GetHashCode(ReadOnlyMemory<byte> obj)
    {
        HashCode hash = new();
        ReadOnlySpan<byte> span = obj.Span;
        int length = Math.Min(span.Length, 32);

        for (int i = 0; i < length; i++)
        {
            hash.Add(span[i]);
        }

        hash.Add(obj.Length);

        return hash.ToHashCode();
    }
}

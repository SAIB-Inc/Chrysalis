namespace Chrysalis.Codec.Serialization.Utils;

public sealed class ReadOnlyMemoryComparer : IEqualityComparer<ReadOnlyMemory<byte>>
{
    public static readonly ReadOnlyMemoryComparer Instance = new();

    public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y) =>
        x.Span.SequenceEqual(y.Span);

    public int GetHashCode(ReadOnlyMemory<byte> obj)
    {
        HashCode hc = new();
        hc.AddBytes(obj.Span);
        return hc.ToHashCode();
    }
}

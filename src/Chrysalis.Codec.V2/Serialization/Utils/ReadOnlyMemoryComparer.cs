namespace Chrysalis.Codec.V2.Serialization.Utils;

public sealed class ReadOnlyMemoryComparer : IEqualityComparer<ReadOnlyMemory<byte>>
{
    public static readonly ReadOnlyMemoryComparer Instance = new();

    public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y) =>
        x.Span.SequenceEqual(y.Span);

    public int GetHashCode(ReadOnlyMemory<byte> obj)
    {
        ReadOnlySpan<byte> span = obj.Span;
        if (span.Length >= 32)
        {
            HashCode hc = new();
            hc.AddBytes(span[..32]);
            return hc.ToHashCode();
        }

        HashCode hc2 = new();
        hc2.AddBytes(span);
        return hc2.ToHashCode();
    }
}

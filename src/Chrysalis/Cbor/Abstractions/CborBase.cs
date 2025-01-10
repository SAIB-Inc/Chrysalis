namespace Chrysalis.Cbor.Abstractions;

public interface ICbor { }

public abstract record CborBase : ICbor
{
    public ReadOnlyMemory<byte>? Raw { get; set; }

    public byte[] GetRawBytes()
    {
        if (!Raw.HasValue || Raw.Value.IsEmpty)
            return Array.Empty<byte>();

        return Raw.Value.ToArray();
    }
}
namespace Chrysalis.Cbor.Types;

public interface ICbor { }

public abstract record CborBase : ICbor
{
    public byte[]? Raw { get; set; }
}
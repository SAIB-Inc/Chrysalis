namespace Chrysalis.Cbor.Abstractions;

public interface ICborConverter
{
    byte[] Serialize(CborBase data);
    public T Deserialize<T>(ReadOnlyMemory<byte> data) where T : CborBase;
}
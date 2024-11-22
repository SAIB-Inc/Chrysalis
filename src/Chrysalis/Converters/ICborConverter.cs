namespace Chrysalis.Converters;

public interface ICborConverter<T>
{
    ReadOnlyMemory<byte> Serialize(T data);
    T Deserialize(ReadOnlyMemory<byte> data);
}
using Chrysalis.Types;

namespace Chrysalis.Converters;

public interface ICborConverter
{
    byte[] Serialize(Cbor data);
    public T Deserialize<T>(byte[] data) where T : Cbor;
}
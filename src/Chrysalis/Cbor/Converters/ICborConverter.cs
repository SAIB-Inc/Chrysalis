using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public interface ICborConverter
{
    byte[] Serialize(CborBase data);
    public T Deserialize<T>(byte[] data) where T : CborBase;
}
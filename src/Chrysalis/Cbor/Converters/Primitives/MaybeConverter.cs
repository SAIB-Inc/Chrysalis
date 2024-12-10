using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class MaybeConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        throw new NotImplementedException();
    }

    public byte[] Serialize(CborBase data)
    {
        throw new NotImplementedException();
    }
}
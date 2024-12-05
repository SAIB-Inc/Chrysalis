using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class MaybeConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : Cbor
    {
        throw new NotImplementedException();
    }

    public byte[] Serialize(Cbor data)
    {
        throw new NotImplementedException();
    }
}
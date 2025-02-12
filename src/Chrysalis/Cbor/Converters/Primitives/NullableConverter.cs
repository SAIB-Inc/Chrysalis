using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class NullableConverter : ICborConverter
{
    public object? Deserialize(CborReader reader, CborOptions? options = null)
    {
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
        }

        return null;
    }

    public void Serialize(CborWriter writer, object? value, CborOptions? options = null)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
    }
}
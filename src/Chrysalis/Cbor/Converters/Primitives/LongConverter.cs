using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class LongConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        return reader.ReadInt64();
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        writer.WriteInt64((long)value);
    }
}
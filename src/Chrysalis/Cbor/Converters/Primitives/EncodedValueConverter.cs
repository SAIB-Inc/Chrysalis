using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class EncodedValueConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        return reader.ReadByteString();
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        writer.WriteByteString((byte[])value);
    }
}
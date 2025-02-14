using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class UlongConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        return reader.ReadUInt64();
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        writer.WriteUInt64((ulong)value);
    }
}
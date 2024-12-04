using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class LongConverter : ICborConverter<CborLong>
{
    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);
        return new CborLong(reader.ReadInt64());
    }

    public byte[] Serialize(CborLong data)
    {
        CborWriter writer = new();
        writer.WriteInt64(data.Value);
        return writer.Encode();
    }
}
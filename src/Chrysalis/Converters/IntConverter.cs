using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class IntConverter : ICborConverter<CborInt>
{
    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);
        return new CborInt(reader.ReadInt32());
    }

    public byte[] Serialize(CborInt data)
    {
        CborWriter writer = new();
        writer.WriteInt32(data.Value);
        return writer.Encode();
    }
}
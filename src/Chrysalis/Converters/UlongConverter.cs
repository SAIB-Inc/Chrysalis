using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class UlongConverter : ICborConverter<CborUlong>
{
    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);
        return new CborUlong(reader.ReadUInt64());
    }

    public byte[] Serialize(CborUlong data)
    {
        CborWriter writer = new();
        writer.WriteUInt64(data.Value);
        return writer.Encode();
    }
}
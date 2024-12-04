using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class BoolConverter : ICborConverter<CborBool>
{
    public byte[] Serialize(CborBool data)
    {
        CborWriter writer = new();
        writer.WriteBoolean(data.Value);
        return [.. writer.Encode()];
    }

    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);
        return new CborBool(reader.ReadBoolean());
    }
}
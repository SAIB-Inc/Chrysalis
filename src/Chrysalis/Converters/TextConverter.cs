using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class TextConverter : ICborConverter<CborText>
{
    public byte[] Serialize(CborText value)
    {
        CborWriter writer = new();
        writer.WriteTextString(value.Value);
        return [.. writer.Encode()];
    }

    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);
        return new CborText(reader.ReadTextString());
    }
}
using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class MaybeConverter<T> : ICborConverter<CborMaybe<T>>
   where T : ICbor, new()
{
    public byte[] Serialize(CborMaybe<T> value)
    {
        CborWriter writer = new();

        if (value.Value is null)
            writer.WriteNull();
        else
            writer.WriteEncodedValue(CborSerializer.Serialize(value.Value));

        return [.. writer.Encode()];
    }

    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);

        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return new CborMaybe<T>(default);
        }

        byte[] valueBytes = reader.ReadEncodedValue().ToArray();
        return new CborMaybe<T>(CborSerializer.Deserialize<T>(valueBytes));
    }
}
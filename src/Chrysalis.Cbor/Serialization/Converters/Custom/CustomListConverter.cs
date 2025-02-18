using System.Formats.Cbor;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public sealed class CustomListConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        return CustomListSerializationUtil.Read(reader, options);
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        int count = value.Count(v => v is not null);
        writer.WriteStartArray(options.IsDefinite ? count : null);

        if (value.Count > 0)
            CustomListSerializationUtil.Write(writer, value);

        writer.WriteEndArray();
    }
}
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
        // Calculate the number of non-null elements
        int count = value.Count(v => v != null);

        // Write the array start with the correct count
        writer.WriteStartArray(options.IsDefinite ? count : null);

        // Write only non-null elements
        CustomListSerializationUtil.Write(writer, value);

        writer.WriteEndArray();
    }
}
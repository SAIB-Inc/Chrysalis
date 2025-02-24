using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public class EnforcedIntConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        int value = reader.ReadInt32();
        if (value != options.Index)
            throw new CborException("Invalid value");

        return value;
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        if (value[0] is not int intValue)
            throw new CborException("Invalid value");

        writer.WriteInt32(intValue);
    }
}
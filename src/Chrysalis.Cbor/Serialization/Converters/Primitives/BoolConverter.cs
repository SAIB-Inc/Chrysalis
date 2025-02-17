using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class BoolConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        return reader.ReadBoolean();
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        if (value is not bool v)
            throw new CborTypeMismatchException("Value is not a boolean", typeof(bool));

        writer.WriteBoolean(v);
    }
}
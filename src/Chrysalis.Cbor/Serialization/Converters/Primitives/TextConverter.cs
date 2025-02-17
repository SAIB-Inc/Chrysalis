using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class TextConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        return reader.ReadTextString();
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        if (value is not string v)
            throw new CborTypeMismatchException("Value is not a string", typeof(string));

        writer.WriteTextString(v);
    }
}
using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class LongConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        return reader.ReadInt64();
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        if (value is not long v)
            throw new CborTypeMismatchException("Value is not a long", typeof(long));

        writer.WriteInt64(v);
    }
}
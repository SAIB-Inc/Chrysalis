using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class IntConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        return reader.ReadInt32();
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        if (value is not int v)
            throw new CborTypeMismatchException("Value is not a int", typeof(int));

        writer.WriteInt32(v);
    }
}
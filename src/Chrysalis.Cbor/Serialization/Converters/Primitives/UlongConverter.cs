using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class UlongConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        return reader.ReadUInt64();
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        if (value is not ulong v)
            throw new CborTypeMismatchException("Value is not a ulong", typeof(ulong));

        writer.WriteUInt64(v);
    }
}
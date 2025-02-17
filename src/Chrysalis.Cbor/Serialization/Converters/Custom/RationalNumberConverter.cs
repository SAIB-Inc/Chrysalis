using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public sealed class RationalNumberConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        reader.ReadStartArray();
        ulong numerator = reader.ReadUInt64();
        ulong denominator = reader.ReadUInt64();
        reader.ReadEndArray();

        return new object[] { numerator, denominator };
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        if (value is not ulong[] array || array.Length != 2)
        {
            throw new CborTypeMismatchException("Value is not a rational number", typeof(ulong[]));
        }

        ulong numerator = array[0];
        ulong denominator = array[1];

        writer.WriteStartArray(2);
        writer.WriteUInt64(numerator);
        writer.WriteUInt64(denominator);
        writer.WriteEndArray();
    }
}
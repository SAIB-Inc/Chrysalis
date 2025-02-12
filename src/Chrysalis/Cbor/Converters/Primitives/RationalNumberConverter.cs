using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class RationalNumberConverter : ICborConverter
{
    public void Serialize(CborWriter writer, object value, CborOptions? options)
    {
        writer.WriteStartArray(2);
        (ulong numerator, ulong denominator) = ((ulong Numerator, ulong Denominator))value;
        writer.WriteUInt64(numerator);
        writer.WriteUInt64(denominator);
        writer.WriteEndArray();
    }

    public object? Deserialize(CborReader reader, CborOptions? options)
    {
        reader.ReadStartArray();
        ulong numerator = reader.ReadUInt64();
        ulong denominator = reader.ReadUInt64();
        reader.ReadEndArray();
        return (numerator, denominator);
    }
}
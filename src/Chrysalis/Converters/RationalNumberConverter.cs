using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class RationalNumberConverter : ICborConverter<CborRationalNumber>
{
    public byte[] Serialize(CborRationalNumber value)
    {
        CborWriter writer = new();

        // Write as an array of two numbers
        writer.WriteStartArray(2);
        writer.WriteUInt64(value.Numerator);
        writer.WriteUInt64(value.Denominator);
        writer.WriteEndArray();

        return [.. writer.Encode()];
    }

    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);

        // Expect array of 2 elements
        reader.ReadStartArray();

        ulong numerator = reader.ReadUInt64();
        ulong denominator = reader.ReadUInt64();

        reader.ReadEndArray();

        return new CborRationalNumber(numerator, denominator);
    }
}
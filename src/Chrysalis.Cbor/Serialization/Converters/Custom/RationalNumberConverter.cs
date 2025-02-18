using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public sealed class RationalNumberConverter : ICborConverter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Read(CborReader reader, CborOptions options)
    {
        reader.ReadStartArray();
        ulong numerator = reader.ReadUInt64();
        ulong denominator = reader.ReadUInt64();
        reader.ReadEndArray();

        return new object[] { numerator, denominator };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        if (value.Count != 2 ||
            value[0] is not ulong numerator ||
            value[1] is not ulong denominator)
        {
            throw new CborTypeMismatchException("Value is not a rational number", typeof(ulong));
        }

        writer.WriteStartArray(2);
        writer.WriteUInt64(numerator);
        writer.WriteUInt64(denominator);
        writer.WriteEndArray();
    }
}
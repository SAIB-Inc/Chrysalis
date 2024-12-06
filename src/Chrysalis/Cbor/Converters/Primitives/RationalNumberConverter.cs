using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class RationalNumberConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);

        // Expect array of 2 elements
        reader.ReadStartArray();
        ulong numerator = reader.ReadUInt64();
        ulong denominator = reader.ReadUInt64();
        reader.ReadEndArray();

        // Use reflection to create an instance of T
        ConstructorInfo constructor = typeof(T).GetConstructor([typeof(ulong), typeof(ulong)])
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} does not have a constructor that accepts two ulong values.");

        CborBase instance = (T)constructor.Invoke([numerator, denominator]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return (T)constructor.Invoke([numerator, denominator]);
    }

    public byte[] Serialize(CborBase data)
    {
        // numerator is the first ulong property
        PropertyInfo? numeratorProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(ulong) && p.Name != nameof(CborBase.Raw))
            ?? throw new InvalidOperationException("No ulong property found in Cbor object.");

        // denominator is the second ulong property
        PropertyInfo? denominatorProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(ulong) && p.Name != nameof(CborBase.Raw) && p.Name != numeratorProperty.Name)
            ?? throw new InvalidOperationException("No second ulong property found in Cbor object.");

        object? rawNumerator = numeratorProperty.GetValue(data);
        object? rawDenominator = denominatorProperty.GetValue(data);

        if (rawNumerator is not ulong numerator || rawDenominator is not ulong denominator)
            throw new InvalidOperationException("Failed to serialize ulong properties.");

        CborWriter writer = new();
        writer.WriteStartArray(2);
        writer.WriteUInt64(numerator);
        writer.WriteUInt64(denominator);
        writer.WriteEndArray();
        return writer.Encode();
    }
}
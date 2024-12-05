using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class RationalNumberConverter : ICborConverter
{
    // public byte[] Serialize(CborRationalNumber value)
    // {
    //     CborWriter writer = new();

    //     // Write as an array of two numbers
    //     writer.WriteStartArray(2);
    //     writer.WriteUInt64(value.Numerator);
    //     writer.WriteUInt64(value.Denominator);
    //     writer.WriteEndArray();

    //     return [.. writer.Encode()];
    // }

    // public ICbor Deserialize(byte[] data, Type? targetType = null)
    // {
    //     CborReader reader = new(data);

    //     // Expect array of 2 elements
    //     reader.ReadStartArray();

    //     ulong numerator = reader.ReadUInt64();
    //     ulong denominator = reader.ReadUInt64();

    //     reader.ReadEndArray();

    //     return new CborRationalNumber(numerator, denominator);
    // }
    public T Deserialize<T>(byte[] data) where T : Cbor
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

        Cbor instance = (T)constructor.Invoke([numerator, denominator]);
        instance.Raw = data;

        // Dynamically create the instance of T
        return (T)constructor.Invoke([numerator, denominator]);
    }

    public byte[] Serialize(Cbor data)
    {
        // numerator is the first ulong property
        PropertyInfo? numeratorProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(ulong) && p.Name != nameof(Cbor.Raw))
            ?? throw new InvalidOperationException("No ulong property found in Cbor object.");

        // denominator is the second ulong property
        PropertyInfo? denominatorProperty = data.GetType().GetProperties().FirstOrDefault(p => p.PropertyType == typeof(ulong) && p.Name != nameof(Cbor.Raw) && p.Name != numeratorProperty.Name)
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
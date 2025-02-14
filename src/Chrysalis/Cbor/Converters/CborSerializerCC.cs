using Chrysalis.Cbor.Types;
using System.Formats.Cbor;
using Chrysalis.Cbor.Utils.Exceptions;

namespace Chrysalis.Cbor.Converters;

public static partial class CborSerializerCore
{
    public static readonly Registry Registry = new();

    static void CborSerializer()
    {
        Registry.Initialize();
    }

    public static object? TryDeserialize(byte[] data, Type type)
    {
        try
        {
            var reader = new CborReader(data);
            var options = Registry.GetOptions(type);

            ValidateTag(reader, options);

            var converter = Registry.GetConverter(
                options.ConverterType ?? throw new InvalidOperationException("No converter type specified"));

            var result = converter.Deserialize(reader, options);

            if (result is CborBase cborBase)
            {
                cborBase.Raw = data;
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to deserialize type {type.Name}: {ex.Message}");
            return null;
        }
    }

    public static void Serialize(CborWriter writer, object value)
    {
        var type = value.GetType();
        var options = Registry.GetOptions(type);
        var converter = Registry.GetConverter(
            options.ConverterType ?? throw new InvalidOperationException("No converter type specified"));

        converter.Serialize(writer, value, options);
    }

    private static void ValidateTag(CborReader reader, CborOptions options)
    {
        if (reader.PeekState() != CborReaderState.Tag) return;

        var tag = reader.ReadTag();
        if (options.Tag.HasValue && tag != (CborTag)options.Tag.Value)
        {
            throw new InvalidOperationException($"Expected tag {options.Tag}, got {tag}");
        }
    }

    // Other helper methods...
    public static byte[] Serialize<T>(T value) where T : CborBase
    {
        var writer = new CborWriter();
        var options = Registry.GetOptions(typeof(T));

        if (options.Tag.HasValue)
        {
            writer.WriteTag((CborTag)options.Tag.Value);
        }

        var converter = Registry.GetConverter(
            options.ConverterType ?? throw new InvalidOperationException("No converter type specified"));

        converter.Serialize(writer, value, options);

        return writer.Encode();
    }

    public static T Deserialize<T>(byte[] data) where T : CborBase
    {
        var reader = new CborReader(data);
        var options = Registry.GetOptions(typeof(T));

        if (options.ConverterType == null)
        {
            // Either set a default converter or throw a more descriptive error
            throw new InvalidOperationException($"No converter type registered for {typeof(T).Name}. Make sure the type is properly registered with a converter.");
        }

        ValidateTag(reader, options);

        var converter = Registry.GetConverter(
            options.ConverterType ?? throw new InvalidOperationException("No converter type specified"));

        var result = (T)converter.Deserialize(reader, options)!;
        result.Raw = data;

        return result;
    }

    internal static object? Deserialize(CborReader reader, CborOptions options)
    {
        ValidateTag(reader, options);

        var converter = Registry.GetConverter(
            options.ConverterType ?? throw new InvalidOperationException("No converter type specified"));

        var result = converter.Deserialize(reader, options);

        if (result is CborBase cborBase)
        {
            // cborBase.Raw = reader.GetRemainingBytes().ToArray();
        }

        return result;
    }

    internal static void Serialize(CborWriter writer, object value, CborOptions options)
    {
        var converter = Registry.GetConverter(
            options.ConverterType ?? throw new InvalidOperationException("No converter type specified"));

        if (options.Tag.HasValue)
        {
            writer.WriteTag((CborTag)options.Tag.Value);
        }

        converter.Serialize(writer, value, options);
    }
}
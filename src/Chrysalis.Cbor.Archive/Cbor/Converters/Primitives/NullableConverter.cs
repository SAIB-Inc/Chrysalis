using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class NullableConverter : ICborConverter
{
    public object? Deserialize(CborReader reader, CborOptions? options = null)
    {
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }
        Type underlyingType = options?.ActivatorType?.GetGenericArguments()[0] ??
            throw new InvalidOperationException("Underlying type not specified in options");
        CborOptions underlyingOptions = CborSerializer.GetOptions(underlyingType) ??
            throw new InvalidOperationException("Underlying options not found");
        return CborSerializer.Deserialize(reader, underlyingOptions);
    }

    public void Serialize(CborWriter writer, object? value, CborOptions? options = null)
    {
        // @TODO: not properly implemented
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        Type underlyingType = options?.ConverterType ??
            throw new InvalidOperationException("Underlying type not specified in options");

        CborOptions? valueOptions = CborSerializer.GetOptions(underlyingType);

        // CborSerializer.Serialize(writer, value, valueOptions);
    }
}
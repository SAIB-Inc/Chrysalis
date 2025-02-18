using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class NullableConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        if (options.RuntimeType is null)
            throw new CborDeserializationException("Runtime type not specified");

        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        Type innerType = options.RuntimeType.GetGenericArguments()[0];
        CborOptions innerOptions = CborRegistry.Instance.GetOptions(innerType);
        return CborSerializer.Deserialize(reader, innerOptions);
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        if (value.First() is null)
        {
            writer.WriteNull();
            return;
        }

        Type innerType = options.RuntimeType!.GetGenericArguments()[0];
        CborOptions innerOptions = CborRegistry.Instance.GetOptions(innerType);
        CborSerializer.Serialize(writer, value.First(), innerOptions);
    }
}
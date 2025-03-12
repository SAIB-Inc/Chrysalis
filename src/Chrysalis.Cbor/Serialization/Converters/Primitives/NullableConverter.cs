using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

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

        return innerType.TryCallStaticRead(reader);
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        if (value.First() is null)
        {
            writer.WriteNull();
            return;
        }

        CborBase? valueCborBase = value[0] as CborBase;
        valueCborBase!.Write(writer, value);
    }
}
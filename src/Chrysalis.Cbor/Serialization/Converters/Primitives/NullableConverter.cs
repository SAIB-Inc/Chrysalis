using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class NullableConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        if (options.RuntimeType is null)
            throw new CborException("Runtime type not specified");

        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        Type innerType = options.RuntimeType.GetGenericArguments()[0];
        CborOptions innerOptions = CborRegistry.Instance.GetOptions(innerType);
        return CborSerializer.Deserialize(reader, innerOptions);
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        throw new NotImplementedException();
    }
}
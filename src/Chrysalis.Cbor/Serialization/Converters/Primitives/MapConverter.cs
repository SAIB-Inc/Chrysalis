using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class MapConverter : ICborConverter
{
    public object Read(CborReader reader, CborOptions options)
    {
        if (options.Constructor is null)
            throw new InvalidOperationException("Constructor not specified");

        List<KeyValuePair<object, object?>> entries = MapSerializationUtil.ReadKeyValuePairs(reader, options);
        return MapSerializationUtil.CreateMapInstance(entries, options);
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        throw new NotImplementedException();
    }
}
using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Utils;

public static class CustomMapSerializationUtil
{
    public static object? Read(CborReader reader, CborOptions options)
    {
        ValidateOptions(options);

        reader.ReadStartMap();
        Dictionary<object, object?> items = [];

        bool isIndexBased = options.IndexPropertyMapping?.Count > 0;

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            (object? key, object? value) = ReadKeyValuePair(reader, options, isIndexBased);

            if (key is not null)
            {
                items[key] = value;
            }
        }

        reader.ReadEndMap();
        return items;
    }

    private static void ValidateOptions(CborOptions options)
    {
        bool hasIndexMapping = options.IndexPropertyMapping?.Count > 0;
        bool hasNamedMapping = options.NamedPropertyMapping?.Count > 0;

        if (!hasIndexMapping && !hasNamedMapping)
        {
            throw new InvalidOperationException("Neither index nor named property mapping is defined in options.");
        }
    }

    private static (object? Key, object? Value) ReadKeyValuePair(CborReader reader, CborOptions options, bool isIndexBased)
    {
        object? key = ReadKey(reader, options);
        if (key == null)
        {
            reader.SkipValue();
            return (null, null);
        }

        Type? valueType = ResolveValueType(key, options, isIndexBased);
        if (valueType == null)
        {
            reader.SkipValue();
            return (null, null);
        }

        CborOptions innerOptions = CborRegistry.Instance.GetOptions(valueType);
        object? value = CborSerializer.Deserialize(reader, innerOptions);

        return (key, value);
    }

    private static object? ReadKey(CborReader reader, CborOptions options)
    {
        if (options.NamedPropertyMapping?.Count > 0)
        {
            return reader.ReadTextString();
        }

        if (reader.PeekState() is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
        {
            reader.SkipValue();
            return null;
        }

        return reader.ReadInt64();
    }

    private static Type? ResolveValueType(object key, CborOptions options, bool isIndexBased)
    {
        if (isIndexBased)
        {
            return options.IndexPropertyMapping![Convert.ToInt32(key)];
        }
        else
        {
            return options.NamedPropertyMapping![key.ToString()!];
        }
    }
}
using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public class ExactValueConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        if (options.ExactValue is null)
            throw new CborException("Invalid value");

        Type T = options.RuntimeType!.GetGenericArguments()[0];
        object? deserialized = T.TryCallStaticRead(reader);
        object exactValue = options.ExactValue;
        bool isMatch = deserialized switch
        {
            ICborNumber<ulong> ulongValue => (ulong)exactValue == ulongValue.Value,
            ICborNumber<long> longValue => (long)exactValue == longValue.Value,
            ICborNumber<int> intValue => (int)exactValue == intValue.Value,
            _ => throw new CborException("Invalid type"),
        };

        if (!isMatch)
            throw new CborException($"Expected value {exactValue} but got {deserialized}");

        return deserialized;
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        if (value == null || value.Count != 1)
            throw new CborException("Invalid value list for ExactValue");

        object? tValue = value[0] ?? throw new CborException("Null value for ExactValue");
        Type T = options.RuntimeType!.GetGenericArguments()[0];

        List<object?> filteredProperties = PropertyResolver.GetFilteredProperties(tValue);
        CborBase tValueCbor = (CborBase)tValue;
        tValueCbor.Write(writer, filteredProperties);
    }
}
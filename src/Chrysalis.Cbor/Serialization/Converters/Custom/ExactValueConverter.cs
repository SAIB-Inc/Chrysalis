using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public class ExactValueConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        if (options.ExactValue is null)
            throw new CborException("Invalid value");

        Type T = options.RuntimeType!.GetGenericArguments()[0];
        CborOptions tOptions = CborRegistry.Instance.GetBaseOptions(T);
        object? deserialized = CborSerializer.Deserialize(reader, tOptions);
        object exactValue = options.ExactValue;
        bool isMatch = deserialized switch
        {
            CborUlong ulongValue => (ulong)exactValue == ulongValue.Value,
            CborLong longValue => (long)exactValue == longValue.Value,
            CborInt intValue => (int)exactValue == intValue.Value,
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
    CborOptions tOptions = CborRegistry.Instance.GetBaseOptions(T);

    CborSerializer.Serialize(writer, tValue, tOptions);
}
}
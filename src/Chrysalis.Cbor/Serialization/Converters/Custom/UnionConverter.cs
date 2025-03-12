using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public sealed class UnionConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        if (options.UnionTypes is null || options.UnionTypes.Count == 0)
            throw new CborDeserializationException("Union types are not defined in options.");

        ReadOnlyMemory<byte> data = reader.ReadEncodedValue();
        IEnumerable<Type> concreteTypes = options.UnionTypes;
        Dictionary<Type, Exception> errors = [];

        foreach (Type type in concreteTypes)
        {
            try
            {
                CborReader innerReader = new(data, CborConformanceMode.Lax);

                // Create new options with the same context
                CborOptions typeOptions = CborRegistry.Instance.GetBaseOptions(type);

                object? value = typeOptions.RuntimeType!.TryCallStaticRead(innerReader);
                options.RuntimeType = type;

                return value;
            }
            catch (Exception ex)
            {
                errors[type] = ex;
            }
        }

        UnionSerializationUtil.ThrowDeserializationError(errors, data.ToArray());
        return null; // Never reached, just for compiler
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        List<object?> filteredProperties = PropertyResolver.GetFilteredProperties(value);
        CborBase cborBase = (CborBase)value[0]! ?? throw new CborSerializationException("Null value for Union");
        cborBase.Write(writer, filteredProperties);
    }
}
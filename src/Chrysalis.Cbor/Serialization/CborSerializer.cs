using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization;

// Main serialization orchestrator
public static class CborSerializer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(T value) where T : CborBase
    {
        if (value.Raw is not null) return value.Raw;

        CborWriter writer = new(CborConformanceMode.Lax);
        CborOptions options = CborRegistry.Instance.GetOptions(typeof(T));
        Serialize(writer, value, options);

        return writer.Encode();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Serialize(CborWriter writer, object? value, CborOptions options)
    {
        try
        {
            if (value is null)
                throw new InvalidOperationException("Value cannot be null");

            Type resolvedType = value.GetType();
            CborOptions resolvedOptions = CborRegistry.Instance.GetOptions(resolvedType);
            Type converterType = resolvedOptions.ConverterType ?? throw new InvalidOperationException("No converter type specified");
            ICborConverter converter = CborRegistry.Instance.GetConverter(converterType);
            List<object?> filteredProperties = PropertyResolver.GetFilteredProperties(value);
            resolvedOptions.RuntimeType = resolvedType;
            CborUtil.WriteTag(writer, options.Tag);
            converter.Write(writer, filteredProperties, options);
        }
        catch (Exception e)
        {
            //throw new Exception($"Failed to serialize object with value {value}, options: {options}", e);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data) where T : CborBase
    {
        CborReader reader = new(data, CborConformanceMode.Lax);
        CborOptions options = CborRegistry.Instance.GetOptions(typeof(T));
        object? deserializedValue = Deserialize(reader, options);
        CborBase instance = (CborBase?)deserializedValue ?? throw new InvalidOperationException("Deserialized value is null");
        instance.Raw = data.ToArray();

        return (T)instance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static object? Deserialize(CborReader reader, CborOptions options)
    {
        Type converterType = options.ConverterType ?? throw new InvalidOperationException("No converter type specified");
        ICborConverter converter = CborRegistry.Instance.GetConverter(converterType);

        // First step if to check if the cbor is tagged
        CborUtil.ReadAndVerifyTag(reader, options.Tag);

        object? value = converter.Read(reader, options);

        if (options.RuntimeType is null)
            throw new InvalidOperationException("Runtime type not specified");

        if (value?.GetType() == options.RuntimeType)
            return value;

        if (options.RuntimeType?.IsAbstract == false)
            return ActivatorUtil.CreateInstance(options.RuntimeType, value, options);

        return value;
    }
}
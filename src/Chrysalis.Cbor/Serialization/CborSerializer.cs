using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
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
        // var writer = new CborWriter();
        // Serialize(writer, value);
        // return writer.ToArray();
        throw new NotImplementedException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Serialize<T>(CborWriter writer, T value, CborOptions options) where T : CborBase
    {
        // var converter = GetConverter<T>();
        // var options = GetOptions<T>();  // Gets options from attributes
        // converter.Write(writer, value, options);
        throw new NotImplementedException();
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
        CborUtils.ReadAndVerifyTag(reader, options.Tag);

        object? value = converter switch
        {
            UnionConverter => UnionSerializationUtil.Read(reader.ReadEncodedValue(true), options),
            CustomConstrConverter => CustomConstrSerializationUtil.Read(reader, options),
            CustomListConverter => CustomListSerializationUtil.Read(reader, options),
            CustomMapConverter => CustomMapSerializationUtil.Read(reader, options),
            ConstrConverter => ConstrSerializationUtil.Read(reader, options),
            _ => converter.Read(reader, options)
        };

        if (options.RuntimeType is null)
            throw new InvalidOperationException("Runtime type not specified");

        if (value?.GetType() == options.RuntimeType)
            return value;

        if (options.RuntimeType?.IsAbstract == false)
            return ActivatorUtil.CreateInstance(options.RuntimeType, value, options);

        return value;
    }
}
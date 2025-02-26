using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Provides methods to serialize and deserialize objects to and from CBOR.
/// </summary>
public static class CborSerializer
{
    /// <summary>
    /// Serializes a CborBase-derived object to a Concise Binary Object Representation (CBOR) byte array.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize, which must inherit from CborBase.</typeparam>
    /// <param name="value">The object instance to serialize. If this object already contains serialized data in its Raw property, that data will be returned directly.</param>
    /// <returns>A byte array containing the CBOR-encoded representation of the object.</returns>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Checks if the object already contains pre-serialized data (Raw property)
    /// 2. If not, creates a new CborWriter with Lax conformance mode
    /// 3. Retrieves type-specific serialization options from the CborRegistry
    /// 4. Delegates to the appropriate serialization implementation
    /// 5. Encodes and returns the final byte array
    /// </remarks>
    /// <exception cref="CborSerializationException">Thrown when an error occurs during serialization.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize(CborBase value)
    {
        if (value.Raw is not null) return value.Raw.Value.ToArray();

        CborWriter writer = new(CborConformanceMode.Lax);
        CborOptions options = CborRegistry.Instance.GetBaseOptions(value.GetType());
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
            CborOptions resolvedOptions = CborRegistry.Instance.GetBaseOptions(resolvedType);
            Type converterType = resolvedOptions.ConverterType ?? throw new InvalidOperationException("No converter type specified");
            ICborConverter converter = CborRegistry.Instance.GetConverter(converterType);
            List<object?> filteredProperties = PropertyResolver.GetFilteredProperties(value);
            resolvedOptions.RuntimeType = resolvedType;
            CborUtil.WriteTag(writer, options.Tag);
            converter.Write(writer, filteredProperties, options);
        }
        catch (Exception e)
        {
            throw new Exception($"Failed to serialize object with value {value}, options: {options}", e);
        }
    }

    /// <summary>
    /// Deserializes a CBOR byte array into an object of type T.
    /// </summary>
    /// <typeparam name="T">The target type for deserialization, which must inherit from CborBase.</typeparam>
    /// <param name="data">The CBOR-encoded byte array to deserialize.</param>
    /// <returns>A deserialized instance of type T with its Raw property set to the input data.</returns>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Creates a new CborReader with the provided data and Lax conformance mode
    /// 2. Retrieves type-specific deserialization options from the CborRegistry
    /// 3. Delegates to the appropriate deserialization implementation
    /// 4. Casts the result to CborBase and validates it is non-null
    /// 5. Stores the original byte array in the instance's Raw property
    /// 6. Returns the typed instance
    /// </remarks>
    /// <exception cref="CborDeserializationException">Thrown when an error occurs during deserialization.</exception>
    /// <exception cref="CborTypeMismatchException">Thrown when the deserialized type does not match the expected type.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data) where T : CborBase
    {
        try
        {
            CborReader reader = new(data, CborConformanceMode.Lax);
            CborOptions options = CborRegistry.Instance.GetBaseOptions(typeof(T));
            CborBase instance = Deserialize(reader, options, false);
            instance.Raw = data.ToArray();

            return (T)instance;
        }
        catch (Exception e)
        {
            throw new Exception($"Failed to deserialize object with data {data}", e);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static CborBase Deserialize(CborReader reader, CborOptions options, bool readChunk = true)
    {

        ReadOnlyMemory<byte>? chunk = null;
        if (readChunk)
        {
            chunk = reader.ReadEncodedValue();
            reader = new CborReader(chunk.Value, CborConformanceMode.Lax);
        }

        Type converterType = options.ConverterType ?? throw new CborDeserializationException("No converter type specified");
        ICborConverter converter = CborRegistry.Instance.GetConverter(converterType);
        CborUtil.ReadAndVerifyTag(reader, options.Tag);
        object? value = converter.Read(reader, options);

        if (options.RuntimeType is null)
            throw new CborDeserializationException("Runtime type not specified");

        CborBase? instance;

        // If the value is already of the correct type, use it directly
        if (value != null && options.RuntimeType.IsAssignableFrom(value.GetType()))
        {
            instance = (CborBase)value;
        }
        else
        {
            // Otherwise create a new instance
            instance = (!options.RuntimeType.IsAbstract ?
                (CborBase)ActivatorUtil.CreateInstance(options.RuntimeType, value, options) :
                (CborBase)value!) ?? throw new CborDeserializationException("Failed to create instance of type");
        }

        if (readChunk && chunk.HasValue)
            instance.Raw = chunk;

        return instance;
    }
}
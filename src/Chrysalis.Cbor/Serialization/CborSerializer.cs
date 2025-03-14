// using System.Formats.Cbor;
// using System.Runtime.CompilerServices;
// using Chrysalis.Cbor.Types;


// namespace Chrysalis.Cbor.Serialization;

// /// <summary>
// /// Provides methods to serialize and deserialize objects to and from CBOR.
// /// </summary>
// public static class CborSerializer
// {
//     /// <summary>
//     /// Serializes a CborBase-derived object to a Concise Binary Object Representation (CBOR) byte array.
//     /// </summary>
//     /// <typeparam name="T">The type of object to serialize, which must inherit from CborBase.</typeparam>
//     /// <param name="value">The object instance to serialize. If this object already contains serialized data in its Raw property, that data will be returned directly.</param>
//     /// <returns>A byte array containing the CBOR-encoded representation of the object.</returns>
//     /// <remarks>
//     /// This method performs the following steps:
//     /// 1. Checks if the object already contains pre-serialized data (Raw property)
//     /// 2. If not, creates a new CborWriter with Lax conformance mode
//     /// 3. Retrieves type-specific serialization options from the CborRegistry
//     /// 4. Delegates to the appropriate serialization implementation
//     /// 5. Encodes and returns the final byte array
//     /// </remarks>
//     /// <exception cref="CborSerializationException">Thrown when an error occurs during serialization.</exception>
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public static byte[] Serialize<T>(CborBase<T> value) where T : CborBase<T>
//     {
//         if (value.Raw is not null) return value.Raw.Value.ToArray();

//         CborWriter writer = new(CborConformanceMode.Lax);
//         List<object?> filteredProperties = PropertyResolver.GetFilteredProperties(value);
//         value.Write(writer, filteredProperties);

//         return writer.Encode();
//     }

//     /// <summary>
//     /// Deserializes a CBOR byte array into an object of type T.
//     /// </summary>
//     /// <typeparam name="T">The target type for deserialization, which must inherit from CborBase.</typeparam>
//     /// <param name="data">The CBOR-encoded byte array to deserialize.</param>
//     /// <returns>A deserialized instance of type T with its Raw property set to the input data.</returns>
//     /// <remarks>
//     /// This method performs the following steps:
//     /// 1. Creates a new CborReader with the provided data and Lax conformance mode
//     /// 2. Retrieves type-specific deserialization options from the CborRegistry
//     /// 3. Delegates to the appropriate deserialization implementation
//     /// 4. Casts the result to CborBase and validates it is non-null
//     /// 5. Stores the original byte array in the instance's Raw property
//     /// 6. Returns the typed instance
//     /// </remarks>
//     /// <exception cref="CborDeserializationException">Thrown when an error occurs during deserialization.</exception>
//     /// <exception cref="CborTypeMismatchException">Thrown when the deserialized type does not match the expected type.</exception>
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public static T Deserialize<T>(ReadOnlyMemory<byte> data) where T : CborBase<T>
//     {
//         try
//         {
//             CborReader reader = new(data, CborConformanceMode.Lax);
//             CborBase<T>? instance = typeof(T).TryCallStaticRead(reader) as CborBase<T>;
//             instance!.Raw = data;

//             return (T)instance;
//         }
//         catch (Exception e)
//         {
//             throw new Exception($"Failed to deserialize object with data {data}", e);
//         }
//     }


//     /// <summary>
//     /// Extension method that tries to call a static Read method if available
//     /// </summary>
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public static object? TryCallStaticRead(this Type type, CborReader reader)
//     {
//         try
//         {
//             // Get the static Read method if it exists
//             var method = type.GetMethod("Read",
//                 System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
//                 null,
//                 [typeof(CborReader)],
//                 null);

//             // If the method exists, invoke it
//             if (method != null)
//             {
//                 return method.Invoke(null, [reader]);
//             }
//         }
//         catch
//         {
//             // If anything fails, we'll fall back to the normal path
//         }

//         // Return null to indicate we couldn't call the static method
//         return null;
//     }


//     // [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     // internal static CborBase Deserialize(CborReader reader, CborOptions options, bool readChunk = true)
//     // {

//     //     ReadOnlyMemory<byte>? chunk = null;
//     //     if (readChunk)
//     //     {
//     //         chunk = reader.ReadEncodedValue();
//     //         reader = new CborReader(chunk.Value, CborConformanceMode.Lax);
//     //     }

//     //     Type converterType = options.ConverterType ?? throw new CborDeserializationException("No converter type specified");
//     //     ICborConverter converter = CborRegistry.Instance.GetConverter(converterType);
//     //     CborUtil.ReadAndVerifyTag(reader, options.Tag);
//     //     object? value = converter.Read(reader, options);

//     //     if (options.RuntimeType is null)
//     //         throw new CborDeserializationException("Runtime type not specified");

//     //     CborBase? instance;

//     //     // If the value is already of the correct type, use it directly
//     //     if (value != null && options.RuntimeType.IsAssignableFrom(value.GetType()))
//     //     {
//     //         instance = (CborBase)value;
//     //     }
//     //     else
//     //     {
//     //         // Otherwise create a new instance
//     //         instance = (!options.RuntimeType.IsAbstract ?
//     //             (CborBase)ActivatorUtil.CreateInstance(options.RuntimeType, value, options) :
//     //             (CborBase)value!) ?? throw new CborDeserializationException("Failed to create instance of type");
//     //     }

//     //     if (readChunk && chunk.HasValue)
//     //         instance.Raw = chunk;

//     //     return instance;
//     // }
// }
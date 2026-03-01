using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Utils;
using Chrysalis.Cbor.Types;


namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Provides methods to serialize and deserialize objects to and from CBOR.
/// </summary>
public static class CborSerializer
{
    [ThreadStatic]
    private static bool? t_preserveRawOverride;

    /// <summary>
    /// Gets or sets a value indicating whether deserialization preserves raw CBOR bytes by default.
    /// </summary>
    /// <remarks>
    /// Use <see cref="UsePreserveRaw(bool)"/> to override this setting for a logical async flow.
    /// </remarks>
    public static bool PreserveRawByDefault { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether raw CBOR bytes should be preserved for the current thread.
    /// </summary>
    public static bool ShouldPreserveRaw
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => t_preserveRawOverride ?? PreserveRawByDefault;
    }

    /// <summary>
    /// Temporarily overrides raw CBOR preservation for the current thread.
    /// </summary>
    /// <param name="preserveRaw">Whether to preserve raw CBOR bytes while the scope is active.</param>
    /// <returns>A disposable scope that restores the previous setting when disposed.</returns>
    public static IDisposable UsePreserveRaw(bool preserveRaw)
    {
        bool? previous = t_preserveRawOverride;
        t_preserveRawOverride = preserveRaw;
        return new PreserveRawScope(previous);
    }

    private sealed class PreserveRawScope(bool? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            t_preserveRawOverride = previous;
            _disposed = true;
        }
    }

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
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during serialization.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(T value) where T : CborBase
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Raw is not null)
        {
            return value.Raw.Value.ToArray();
        }

        CborWriter writer = new(CborConformanceMode.Lax);
        GenericSerializationUtil.Write<T>(writer, value);

        return writer.Encode();
    }

    /// <summary>
    /// Serializes a CborBase-derived object to a <see cref="ReadOnlyMemory{T}"/> without copying when pre-serialized data exists.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize, which must inherit from CborBase.</typeparam>
    /// <param name="value">The object instance to serialize.</param>
    /// <returns>A <see cref="ReadOnlyMemory{T}"/> containing the CBOR-encoded representation of the object.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlyMemory<byte> SerializeToMemory<T>(T value) where T : CborBase
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Raw is not null)
        {
            return value.Raw.Value;
        }

        CborWriter writer = new(CborConformanceMode.Lax);
        GenericSerializationUtil.Write<T>(writer, value);

        return writer.Encode();
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
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during deserialization or when the deserialized type does not match the expected type.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data) where T : CborBase
    {
        CborReader reader = new(data, CborConformanceMode.Lax);
        T? result = GenericSerializationUtil.Read<T>(reader);
        return result ?? throw new InvalidOperationException("Deserialization failed: result is null.");
    }

    /// <summary>
    /// Deserializes a CBOR byte array into an object of type T with raw-byte preservation disabled.
    /// </summary>
    /// <typeparam name="T">The target type for deserialization, which must inherit from CborBase.</typeparam>
    /// <param name="data">The CBOR-encoded byte array to deserialize.</param>
    /// <returns>A deserialized instance of type T.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T DeserializeWithoutRaw<T>(ReadOnlyMemory<byte> data) where T : CborBase
    {
        bool? previous = t_preserveRawOverride;
        t_preserveRawOverride = false;
        try
        {
            CborReader reader = new(data, CborConformanceMode.Lax);
            T? result = GenericSerializationUtil.Read<T>(reader);
            return result ?? throw new InvalidOperationException("Deserialization failed: result is null.");
        }
        finally
        {
            t_preserveRawOverride = previous;
        }
    }

    /// <summary>
    /// Deserializes a single CBOR value from the beginning of the data and reports how many bytes were consumed.
    /// </summary>
    /// <typeparam name="T">The target type for deserialization, which must inherit from CborBase.</typeparam>
    /// <param name="data">The CBOR-encoded data to deserialize. May contain trailing data beyond the first value.</param>
    /// <param name="bytesConsumed">When this method returns, contains the number of bytes consumed from the input.</param>
    /// <returns>A deserialized instance of type T.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlyMemory<byte> data, out int bytesConsumed) where T : CborBase
    {
        CborReader reader = new(data, CborConformanceMode.Lax);
        T? result = GenericSerializationUtil.Read<T>(reader);
        bytesConsumed = data.Length - reader.BytesRemaining;
        return result ?? throw new InvalidOperationException("Deserialization failed: result is null.");
    }
}

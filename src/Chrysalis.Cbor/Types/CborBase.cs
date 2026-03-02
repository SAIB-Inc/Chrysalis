using System.Buffers;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types in the Chrysalis framework.
/// </summary>
public abstract partial record CborBase
{
    /// <summary>
    /// Gets or sets the raw CBOR-encoded byte representation of this value.
    /// </summary>
    public ReadOnlyMemory<byte>? Raw { get; set; }

    /// <summary>
    /// Gets the fully qualified CBOR type name, including generic type parameters if applicable.
    /// </summary>
    public virtual string? CborTypeName
    {
        get
        {
            Type type = GetType();

            if (type.IsGenericType)
            {
                Type genericTypeDef = type.GetGenericTypeDefinition();
                string ns = type.Namespace ?? "";
                string baseName = type.Name;

                int backtickIndex = baseName.IndexOf('`', StringComparison.Ordinal);
                if (backtickIndex > 0)
                {
                    baseName = baseName[..backtickIndex];
                }

                Type[] genericParams = genericTypeDef.GetGenericArguments();
                string typeParams = string.Join(", ", genericParams.Select(p => p.Name));

                return $"{ns}.{baseName}<{typeParams}>";
            }

            return type.FullName;
        }
    }

    /// <summary>
    /// Gets or sets the CBOR constructor index used for Plutus data encoding.
    /// </summary>
    public int ConstrIndex { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this value uses indefinite-length CBOR encoding.
    /// </summary>
    public bool IsIndefinite { get; set; }

    /// <summary>
    /// Reads a CBOR value from the specified byte data.
    /// </summary>
    /// <param name="data">The raw CBOR-encoded bytes to read.</param>
    /// <returns>The deserialized CBOR value.</returns>
    /// <exception cref="NotImplementedException">Always thrown; use the generated serializer instead.</exception>
    public static CborBase Read(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Reads a CBOR value from the specified byte data and reports how many bytes were consumed.
    /// </summary>
    /// <param name="data">The raw CBOR-encoded bytes to read.</param>
    /// <param name="bytesConsumed">The number of bytes consumed from the input.</param>
    /// <returns>The deserialized CBOR value.</returns>
    /// <exception cref="NotImplementedException">Always thrown; use the generated serializer instead.</exception>
    public static CborBase Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Writes a CBOR value to the specified output buffer.
    /// </summary>
    /// <param name="output">The output buffer to write to.</param>
    /// <param name="data">The CBOR value to write.</param>
    /// <exception cref="NotImplementedException">Always thrown; use the generated serializer instead.</exception>
    public static void Write(IBufferWriter<byte> output, CborBase? data)
    {
        throw new NotImplementedException();
    }
}

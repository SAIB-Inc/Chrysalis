using System.Formats.Cbor;

namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Defines methods for converting objects to and from CBOR format.
/// </summary>
public interface ICborConverter
{
    /// <summary>
    /// Writes a list of values to CBOR format using the specified writer and options.
    /// </summary>
    /// <param name="writer">The CBOR writer to write to.</param>
    /// <param name="value">The list of values to serialize.</param>
    /// <param name="options">The serialization options.</param>
    void Write(CborWriter writer, IList<object?> value, CborOptions options);

    /// <summary>
    /// Reads an object from CBOR format using the specified reader and options.
    /// </summary>
    /// <param name="reader">The CBOR reader to read from.</param>
    /// <param name="options">The deserialization options.</param>
    /// <returns>The deserialized object, or null.</returns>
    object? Read(CborReader reader, CborOptions options);
}

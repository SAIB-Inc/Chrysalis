using System.Buffers;

namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Defines methods for converting objects to and from CBOR format.
/// </summary>
public interface ICborConverter
{
    /// <summary>Writes a list of values to CBOR format using the specified output buffer and options.</summary>
    void Write(IBufferWriter<byte> output, IList<object?> value, CborOptions options);

    /// <summary>Reads an object from CBOR-encoded bytes using the specified options.</summary>
    object? Read(ReadOnlyMemory<byte> data, CborOptions options);
}

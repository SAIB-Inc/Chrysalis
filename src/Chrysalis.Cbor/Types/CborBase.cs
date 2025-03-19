using System.Formats.Cbor;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
public abstract partial record CborBase<T>
{
    // Raw serialized data for caching and quick re-serialization
    public ReadOnlyMemory<byte>? Raw { get; set; }

    // Type discriminator for unions (to avoid reflection)
    public string? CborTypeName { get; set; }
}
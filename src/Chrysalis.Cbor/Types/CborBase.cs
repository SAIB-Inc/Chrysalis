using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
public abstract partial record CborBase
{
    // Raw serialized data for caching and quick re-serialization
    public ReadOnlyMemory<byte>? Raw { get; set; }

    // Static methods for serialization/deserialization with converter delegation
    // These are implemented by source generator for each type
    public virtual void Write(CborWriter writer, List<object?> value)
        => throw new NotImplementedException("This method should be implemented by source generator");

    public static object? Read(CborReader reader)
        => throw new NotImplementedException("This method should be implemented by source generator");
}
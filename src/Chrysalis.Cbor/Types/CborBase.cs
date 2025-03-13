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
public abstract partial record CborBase<T>
{
    // Raw serialized data for caching and quick re-serialization
    public ReadOnlyMemory<byte>? Raw { get; set; }

    // Type discriminator for unions (to avoid reflection)
    public string? CborTypeName { get; set; }

    // Static methods for serialization/deserialization with converter delegation
    // These are implemented by source generator for each type
    public static void Write(CborWriter writer, T value)
        => throw new NotImplementedException("This method should be implemented by source generator");

    public static T? Read(ReadOnlyMemory<byte> data, bool preserveRaw = false)
        => throw new NotImplementedException("This method should be implemented by source generator");
}
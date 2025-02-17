using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
public abstract record CborBase
{
    // Raw bytes preserved after deserialization
    public byte[]? Raw;

    // Fast path if raw bytes available
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ToBytes() =>
        Raw ?? CborSerializer.Serialize(this);

    // Optional: Factory method for creating from bytes
    public static T FromBytes<T>(ReadOnlyMemory<byte> bytes) where T : CborBase =>
        CborSerializer.Deserialize<T>(bytes);
}
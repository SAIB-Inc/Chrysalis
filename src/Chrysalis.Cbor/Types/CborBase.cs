using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
public abstract record CborBase
{
    public byte[]? Raw;

    public ReadOnlySpan<byte> ToBytes() =>
        Raw ?? CborSerializer.Serialize(this);
    public static T FromBytes<T>(ReadOnlyMemory<byte> bytes) where T : CborBase =>
        CborSerializer.Deserialize<T>(bytes);
}
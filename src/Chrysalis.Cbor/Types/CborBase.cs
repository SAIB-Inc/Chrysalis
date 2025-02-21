using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
public abstract record CborBase
{
    public ReadOnlyMemory<byte>? Raw;

    public byte[]? ToBytes() => Raw is not null ? Raw.Value.ToArray() : CborSerializer.Serialize(this);

    public static T FromBytes<T>(ReadOnlyMemory<byte> bytes) where T : CborBase => CborSerializer.Deserialize<T>(bytes);
}
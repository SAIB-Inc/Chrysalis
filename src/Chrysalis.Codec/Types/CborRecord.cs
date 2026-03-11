using Chrysalis.Codec.Serialization;

namespace Chrysalis.Codec.Types;

/// <summary>
/// Convenience base class for CBOR-serializable record types.
/// Downstream projects use this instead of implementing ICborType directly.
/// </summary>
public abstract partial record CborRecord : ICborType
{
    /// <inheritdoc />
    public ReadOnlyMemory<byte> Raw { get; set; }

    /// <inheritdoc />
    public int ConstrIndex { get; set; }

    /// <inheritdoc />
    public bool IsIndefinite { get; set; }
}

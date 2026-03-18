using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types;

/// <summary>
/// Represents CBOR-in-CBOR encoded data: #6.24(bytes .cbor T) per Cardano CDDL.
/// The codegen emits tag 24 + bytestring on write and reads past tag 24 on read.
/// <see cref="Value"/> contains the inner CBOR bytes (without the tag/bytestring wrapper).
/// Used for inline datums and script references.
/// </summary>
[CborSerializable]
public partial record CborEncodedValue(ReadOnlyMemory<byte> Value) : ICborType
{
    /// <inheritdoc />
    public ReadOnlyMemory<byte> Raw { get; set; }

    /// <inheritdoc />
    public int ConstrIndex { get; set; }

    /// <inheritdoc />
    public bool IsIndefinite { get; set; }
}

using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Represents a datum option in a transaction output, either as a hash reference or inline data.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record DatumOption : CborBase { }

/// <summary>
/// Represents a datum option that references a datum by its hash.
/// </summary>
/// <param name="Option">The option tag (0 for datum hash).</param>
/// <param name="DatumHash">The hash of the referenced datum.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record DatumHashOption(
    [CborOrder(0)] int Option,
    [CborOrder(1)] ReadOnlyMemory<byte> DatumHash
) : DatumOption, ICborPreserveRaw;

/// <summary>
/// Represents a datum option with inline datum data embedded directly in the output.
/// </summary>
/// <param name="Option">The option tag (1 for inline datum).</param>
/// <param name="Data">The inline datum encoded as a CBOR value.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record InlineDatumOption(
    [CborOrder(0)] int Option,
    [CborOrder(1)] CborEncodedValue Data
) : DatumOption, ICborPreserveRaw;

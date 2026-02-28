using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Cli;

/// <summary>
/// Represents index references for inputs and outputs.
/// </summary>
/// <param name="InputIndex">The input index.</param>
/// <param name="OutputIndices">The output indices.</param>
[CborSerializable]
[CborConstr(0)]
public partial record Indices(
    [CborOrder(0)] ulong InputIndex,
    [CborOrder(1)] OutputIndices OutputIndices
) : CborBase;

/// <summary>
/// Represents indices for main, fee, and change outputs.
/// </summary>
/// <param name="MainIndex">The main output index.</param>
/// <param name="FeeIndex">The fee output index.</param>
/// <param name="ChangeIndex">The change output index.</param>
[CborSerializable]
[CborConstr(0)]
public partial record OutputIndices(
    [CborOrder(0)] ulong MainIndex,
    [CborOrder(1)] ulong FeeIndex,
    [CborOrder(1)] ulong ChangeIndex
) : CborBase;

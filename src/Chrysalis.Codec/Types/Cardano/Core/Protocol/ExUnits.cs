using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Protocol;

/// <summary>
/// Execution units representing the computational resources consumed by a Plutus script.
/// </summary>
/// <param name="Mem">The memory units consumed.</param>
/// <param name="Steps">The CPU steps consumed.</param>
[CborSerializable]
[CborList]
public partial record ExUnits(
    [CborOrder(0)] ulong Mem,
    [CborOrder(1)] ulong Steps
) : CborBase;

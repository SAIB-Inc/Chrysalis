namespace Chrysalis.Plutus.VM.Models;

/// <summary>
/// Represents the execution units (memory and CPU steps) consumed by a Plutus script evaluation.
/// </summary>
/// <param name="Mem">The memory units consumed.</param>
/// <param name="Steps">The CPU steps consumed.</param>
public record ExUnits(ulong Mem, ulong Steps);

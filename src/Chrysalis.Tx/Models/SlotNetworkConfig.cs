namespace Chrysalis.Tx.Models;

/// <summary>
/// Configuration for converting between slot numbers and time for a specific Cardano network.
/// </summary>
/// <param name="ZeroTime">The Unix timestamp (milliseconds) of the genesis block.</param>
/// <param name="ZeroSlot">The slot number of the genesis block.</param>
/// <param name="SlotLength">The slot length in milliseconds.</param>
public record SlotNetworkConfig(long ZeroTime, long ZeroSlot, int SlotLength);

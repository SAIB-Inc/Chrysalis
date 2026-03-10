using Chrysalis.Codec.Types.Cardano.Core.Byron;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Byron;

/// <summary>
/// Extension methods for Byron-era block types to access block properties.
/// </summary>
public static class ByronBlockExtensions
{
    /// <summary>
    /// Gets the protocol magic number from a Byron main block.
    /// </summary>
    /// <param name="self">The Byron main block instance.</param>
    /// <returns>The protocol magic number.</returns>
    public static ulong ProtocolMagic(this ByronMainBlock self) => self.Header.ProtocolMagic;

    /// <summary>
    /// Gets the protocol magic number from a Byron epoch boundary block.
    /// </summary>
    /// <param name="self">The Byron epoch boundary block instance.</param>
    /// <returns>The protocol magic number.</returns>
    public static ulong ProtocolMagic(this ByronEbBlock self) => self.Header.ProtocolMagic;

    /// <summary>
    /// Gets the previous block hash from a Byron main block.
    /// </summary>
    /// <param name="self">The Byron main block instance.</param>
    /// <returns>The previous block hash bytes.</returns>
    public static ReadOnlyMemory<byte> PrevBlock(this ByronMainBlock self) => self.Header.PrevBlock;

    /// <summary>
    /// Gets the previous block hash from a Byron epoch boundary block.
    /// </summary>
    /// <param name="self">The Byron epoch boundary block instance.</param>
    /// <returns>The previous block hash bytes.</returns>
    public static ReadOnlyMemory<byte> PrevBlock(this ByronEbBlock self) => self.Header.PrevBlock;

    /// <summary>
    /// Gets the epoch number from a Byron main block.
    /// </summary>
    /// <param name="self">The Byron main block instance.</param>
    /// <returns>The epoch number.</returns>
    public static ulong Epoch(this ByronMainBlock self) => self.Header.ConsensusData.SlotId.Epoch;

    /// <summary>
    /// Gets the epoch number from a Byron epoch boundary block.
    /// </summary>
    /// <param name="self">The Byron epoch boundary block instance.</param>
    /// <returns>The epoch number.</returns>
    public static ulong Epoch(this ByronEbBlock self) => self.Header.ConsensusData.Epoch;

    /// <summary>
    /// Gets the slot number from a Byron main block.
    /// </summary>
    /// <param name="self">The Byron main block instance.</param>
    /// <returns>The slot number.</returns>
    public static ulong Slot(this ByronMainBlock self) => self.Header.ConsensusData.SlotId.Slot;

    /// <summary>
    /// Gets the Byron transactions from a Byron main block.
    /// </summary>
    /// <param name="self">The Byron main block instance.</param>
    /// <returns>The Byron transaction payloads.</returns>
    public static IEnumerable<ByronTxPayload> ByronTransactions(this ByronMainBlock self) => self.Body.TxPayload.GetValue();
}

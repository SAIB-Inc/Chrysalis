using Chrysalis.Codec.V2.Types.Cardano.Core.Byron;

namespace Chrysalis.Codec.V2.Extensions.Cardano.Core.Byron;

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
    public static ulong ProtocolMagic(this ByronMainBlock self)
    {
        return self.Header.ProtocolMagic;
    }

    /// <summary>
    /// Gets the protocol magic number from a Byron epoch boundary block.
    /// </summary>
    /// <param name="self">The Byron epoch boundary block instance.</param>
    /// <returns>The protocol magic number.</returns>
    public static ulong ProtocolMagic(this ByronEbBlock self)
    {
        return self.Header.ProtocolMagic;
    }

    /// <summary>
    /// Gets the previous block hash from a Byron main block.
    /// </summary>
    /// <param name="self">The Byron main block instance.</param>
    /// <returns>The previous block hash bytes.</returns>
    public static ReadOnlyMemory<byte> PrevBlock(this ByronMainBlock self)
    {
        return self.Header.PrevBlock;
    }

    /// <summary>
    /// Gets the previous block hash from a Byron epoch boundary block.
    /// </summary>
    /// <param name="self">The Byron epoch boundary block instance.</param>
    /// <returns>The previous block hash bytes.</returns>
    public static ReadOnlyMemory<byte> PrevBlock(this ByronEbBlock self)
    {
        return self.Header.PrevBlock;
    }

    /// <summary>
    /// Gets the epoch number from a Byron main block.
    /// </summary>
    /// <param name="self">The Byron main block instance.</param>
    /// <returns>The epoch number.</returns>
    public static ulong Epoch(this ByronMainBlock self)
    {
        return self.Header.ConsensusData.SlotId.Epoch;
    }

    /// <summary>
    /// Gets the epoch number from a Byron epoch boundary block.
    /// </summary>
    /// <param name="self">The Byron epoch boundary block instance.</param>
    /// <returns>The epoch number.</returns>
    public static ulong Epoch(this ByronEbBlock self)
    {
        return self.Header.ConsensusData.Epoch;
    }

    /// <summary>
    /// Gets the slot number from a Byron main block.
    /// </summary>
    /// <param name="self">The Byron main block instance.</param>
    /// <returns>The slot number.</returns>
    public static ulong Slot(this ByronMainBlock self)
    {
        return self.Header.ConsensusData.SlotId.Slot;
    }

    /// <summary>
    /// Gets the Byron transactions from a Byron main block.
    /// </summary>
    /// <param name="self">The Byron main block instance.</param>
    /// <returns>The Byron transaction payloads.</returns>
    public static IEnumerable<ByronTxPayload> ByronTransactions(this ByronMainBlock self)
    {
        return self.Body.TxPayload.GetValue();
    }
}

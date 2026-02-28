using Chrysalis.Cbor.Types.Cardano.Core.Byron;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Byron;

public static class ByronBlockExtensions
{
    public static uint ProtocolMagic(this ByronMainBlock self) =>
        self.Header.ProtocolMagic;

    public static uint ProtocolMagic(this ByronEbBlock self) =>
        self.Header.ProtocolMagic;

    public static byte[] PrevBlock(this ByronMainBlock self) =>
        self.Header.PrevBlock;

    public static byte[] PrevBlock(this ByronEbBlock self) =>
        self.Header.PrevBlock;

    public static ulong Epoch(this ByronMainBlock self) =>
        self.Header.ConsensusData.SlotId.Epoch;

    public static ulong Epoch(this ByronEbBlock self) =>
        self.Header.ConsensusData.EpochId;

    public static ulong Slot(this ByronMainBlock self) =>
        self.Header.ConsensusData.SlotId.Slot;

    public static IEnumerable<ByronTxPayload> ByronTransactions(this ByronMainBlock self) =>
        self.Body.TxPayload.GetValue();
}

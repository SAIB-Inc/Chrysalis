using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Byron;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Network.Cbor.ChainSync;

/// <summary>
/// Represents the N2N ChainSync header payload, providing a unified API
/// for accessing block metadata (slot, hash, height) across all Cardano eras.
/// Byron: [0, [[prefix_a, prefix_b], tag24(header_bytes)]]
/// Shelley+: [variant, tag24(header_bytes)]
/// </summary>
public record ChainSyncHeader(
    byte Variant,
    byte? ByronSubTag,
    ReadOnlyMemory<byte> HeaderCbor
)
{
    /// <summary>Whether this header is from the Byron era (variant 0).</summary>
    public bool IsByron => Variant == 0;

    /// <summary>Whether this is a Byron Epoch Boundary Block header.</summary>
    public bool IsByronEbb => IsByron && ByronSubTag == 0;

    /// <summary>
    /// The era name based on the variant number.
    /// </summary>
    public string Era => Variant switch
    {
        0 when IsByronEbb => "Byron EBB",
        0 => "Byron",
        1 => "Shelley",
        2 => "Allegra",
        3 => "Mary",
        4 => "Alonzo",
        5 => "Babbage",
        6 => "Conway",
        _ => $"Era({Variant})"
    };

    /// <summary>
    /// Gets the absolute slot number from this header, regardless of era.
    /// </summary>
    /// <returns>The absolute slot number.</returns>
    public ulong Slot()
    {
        return ExtractPoint().Slot;
    }

    /// <summary>
    /// Computes the Blake2b-256 hash of this header as a lowercase hex string, regardless of era.
    /// </summary>
    /// <returns>The hex-encoded hash string.</returns>
    public string Hash()
    {
        return Convert.ToHexStringLower(ExtractPoint().Hash);
    }

    /// <summary>
    /// Gets the block height from this header, regardless of era.
    /// Byron EBBs return 0 as they don't have block numbers.
    /// </summary>
    /// <returns>The block height.</returns>
    public ulong Height()
    {
        return ExtractPoint().BlockNumber;
    }

    /// <summary>
    /// Extracts the absolute slot number, block hash, and height from this header, regardless of era.
    /// For Byron EBBs, the slot is computed as <c>epoch * 21600</c>.
    /// For Byron main blocks, the slot is <c>(epoch * 21600) + relativeSlot</c>.
    /// For Shelley+ blocks, the slot and hash are read directly from the header body.
    /// </summary>
    /// <returns>A <see cref="ChainPoint"/> containing the slot, hash, and block height.</returns>
    public ChainPoint ExtractPoint()
    {
        if (IsByron)
        {
            if (IsByronEbb)
            {
                ByronEbbHead ebbHead = CborSerializer.Deserialize<ByronEbbHead>(HeaderCbor);
                ulong slot = ebbHead.ConsensusData.EpochId * 21600;
                byte[] hash = BlockExtensions.HashByronHeader(0, HeaderCbor.Span);
                return new ChainPoint(slot, hash, 0);
            }
            else
            {
                ByronBlockHead blockHead = CborSerializer.Deserialize<ByronBlockHead>(HeaderCbor);
                ulong slot = (blockHead.ConsensusData.SlotId.Epoch * 21600) + blockHead.ConsensusData.SlotId.Slot;
                byte[] hash = BlockExtensions.HashByronHeader(1, HeaderCbor.Span);
                ulong height = blockHead.ConsensusData.Difficulty.GetValue().FirstOrDefault();
                return new ChainPoint(slot, hash, height);
            }
        }

        BlockHeader header = CborSerializer.Deserialize<BlockHeader>(HeaderCbor);
        ulong headerSlot = header.HeaderBody.Slot();
        ulong blockNumber = header.HeaderBody.BlockNumber();
        byte[] headerHash = Convert.FromHexString(header.Hash());
        return new ChainPoint(headerSlot, headerHash, blockNumber);
    }

    /// <summary>
    /// Decodes a <see cref="ChainSyncHeader"/> from the raw CBOR payload of a RollForward message.
    /// </summary>
    /// <param name="payload">The raw CBOR bytes from the ChainSync protocol.</param>
    /// <returns>A decoded <see cref="ChainSyncHeader"/>.</returns>
    public static ChainSyncHeader Decode(ReadOnlyMemory<byte> payload)
    {
        CborReader reader = new(payload.Span);
        reader.ReadBeginArray();
        _ = reader.ReadSize();

        byte variant = checked((byte)reader.ReadUInt64());

        byte? byronSubTag = null;
        ReadOnlyMemory<byte> headerCbor;

        if (variant == 0)
        {
            // Byron: [[prefix_a, prefix_b], tag24(header_bytes)]
            reader.ReadBeginArray();
            _ = reader.ReadSize();

            reader.ReadBeginArray();
            _ = reader.ReadSize();
            byronSubTag = checked((byte)reader.ReadUInt64());
            _ = reader.ReadUInt64(); // second prefix value (unused)

            _ = reader.TryReadSemanticTag(out _);
            headerCbor = reader.ReadByteString().ToArray();
        }
        else
        {
            // Shelley+: tag24(header_bytes)
            _ = reader.TryReadSemanticTag(out _);
            headerCbor = reader.ReadByteString().ToArray();
        }

        return new ChainSyncHeader(variant, byronSubTag, headerCbor);
    }
}

/// <summary>
/// Represents an extracted chain point with slot, hash, and block height.
/// </summary>
/// <param name="Slot">The absolute slot number.</param>
/// <param name="Hash">The block hash (Blake2b-256).</param>
/// <param name="BlockNumber">The block height (0 for Byron EBBs).</param>
public readonly record struct ChainPoint(ulong Slot, byte[] Hash, ulong BlockNumber);

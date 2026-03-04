using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Byron;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Network.Cbor.ChainSync;

/// <summary>
/// Represents the N2N ChainSync header payload.
/// Byron: [0, [[prefix_a, prefix_b], tag24(header_bytes)]]
/// Shelley+: [variant, tag24(header_bytes)]
/// </summary>
public record HeaderContent(
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
    /// Extracts the absolute slot number and block hash from this header, regardless of era.
    /// For Byron EBBs, the slot is computed as <c>epoch * 21600</c>.
    /// For Byron main blocks, the slot is <c>(epoch * 21600) + relativeSlot</c>.
    /// For Shelley+ blocks, the slot and hash are read directly from the header body.
    /// </summary>
    /// <returns>A <see cref="HeaderPoint"/> containing the slot, hash, and block height.</returns>
    public HeaderPoint ExtractPoint()
    {
        if (IsByron)
        {
            if (IsByronEbb)
            {
                ByronEbbHead ebbHead = CborSerializer.Deserialize<ByronEbbHead>(HeaderCbor);
                ulong slot = ebbHead.ConsensusData.EpochId * 21600;
                byte[] hash = HashByronHeader(0, HeaderCbor.Span);
                return new HeaderPoint(slot, hash, 0);
            }
            else
            {
                ByronBlockHead blockHead = CborSerializer.Deserialize<ByronBlockHead>(HeaderCbor);
                ulong slot = (blockHead.ConsensusData.SlotId.Epoch * 21600) + blockHead.ConsensusData.SlotId.Slot;
                byte[] hash = HashByronHeader(1, HeaderCbor.Span);
                ulong height = blockHead.ConsensusData.Difficulty.GetValue().FirstOrDefault();
                return new HeaderPoint(slot, hash, height);
            }
        }

        BlockHeader header = CborSerializer.Deserialize<BlockHeader>(HeaderCbor);
        ulong headerSlot = header.HeaderBody.Slot();
        ulong blockNumber = header.HeaderBody.BlockNumber();
        byte[] headerHash = Convert.FromHexString(header.Hash());
        return new HeaderPoint(headerSlot, headerHash, blockNumber);
    }

    /// <summary>
    /// Decodes a HeaderContent from the raw CBOR payload of a RollForward message.
    /// </summary>
    public static HeaderContent Decode(ReadOnlyMemory<byte> payload)
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

        return new HeaderContent(variant, byronSubTag, headerCbor);
    }

    /// <summary>
    /// Computes the Byron block hash by wrapping the header in a CBOR tuple [tag, header_bytes] before hashing.
    /// Byron EBB uses tag=0, Byron main block uses tag=1.
    /// </summary>
    private static byte[] HashByronHeader(byte tag, ReadOnlySpan<byte> headerCbor)
    {
        byte[] wrapped = new byte[2 + headerCbor.Length];
        wrapped[0] = 0x82; // CBOR array(2)
        wrapped[1] = tag;  // CBOR uint(0) or uint(1)
        headerCbor.CopyTo(wrapped.AsSpan(2));
        return Blake2Fast.Blake2b.HashData(32, wrapped);
    }
}

/// <summary>
/// Represents the extracted slot, hash, and block height from a ChainSync header.
/// </summary>
/// <param name="Slot">The absolute slot number.</param>
/// <param name="Hash">The block hash (Blake2b-256).</param>
/// <param name="BlockNumber">The block height (0 for Byron EBBs).</param>
public readonly record struct HeaderPoint(ulong Slot, byte[] Hash, ulong BlockNumber);

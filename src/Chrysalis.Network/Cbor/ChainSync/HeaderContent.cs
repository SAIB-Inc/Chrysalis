using Dahomey.Cbor.Serialization;

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
}

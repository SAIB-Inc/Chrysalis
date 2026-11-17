using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Byron;

namespace Chrysalis.Codec.Types.Cardano.Core;

[CborSerializable]
[CborList]
public readonly partial record struct BlockWithEra : ICborType
{
    internal const ulong MaxSupportedEraTag = 7;

    [CborOrder(0)] public partial int EraNumber { get; }
    [CborOrder(1)]
    [CborUnionHint(nameof(EraNumber), 0, typeof(ByronEbBlock))]
    [CborUnionHint(nameof(EraNumber), 1, typeof(ByronMainBlock))]
    [CborUnionHint(nameof(EraNumber), 2, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 3, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 4, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 5, typeof(AlonzoCompatibleBlock))]
    [CborUnionHint(nameof(EraNumber), 6, typeof(BabbageBlock))]
    [CborUnionHint(nameof(EraNumber), 7, typeof(ConwayBlock))]
    public partial IBlock Block { get; }

    /// <summary>Reads a block with caller-supplied context for an unknown-era error.</summary>
    public static BlockWithEra Read(ReadOnlyMemory<byte> data, (ulong Slot, ReadOnlyMemory<byte> Hash) point) =>
        UnknownEra.ReadAtPoint<BlockWithEra>(data, point);

    /// <summary>Explicitly receives an unsupported block as unprocessed wire bytes.</summary>
    public static UnknownEra ReadUnknownEra(ReadOnlyMemory<byte> data,
        (ulong Slot, ReadOnlyMemory<byte> Hash)? point = null) =>
        UnknownEra.Read(data, EraEnvelope.Block, MaxSupportedEraTag, point);
}

/// <summary>Validates the wire era before a lazy block can be reported as decoded.</summary>
public readonly record struct BlockWithEraValidator : ICborValidator<BlockWithEra>
{
    /// <inheritdoc />
    public bool Validate(BlockWithEra input) => UnknownEra.Validate(input, EraEnvelope.Block, BlockWithEra.MaxSupportedEraTag);
}

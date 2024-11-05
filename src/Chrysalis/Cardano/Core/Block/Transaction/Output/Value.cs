using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(Lovelace), typeof(LovelaceWithMultiAsset)])]
public record Value : RawCbor;

[CborSerializable(CborType.Ulong)]
public record Lovelace(ulong Value): Value;
// @TODO: think about how to support not recreating multiple base types

[CborSerializable(CborType.List)]
public record LovelaceWithMultiAsset(
    [CborProperty(0)] Lovelace Lovelace,
    [CborProperty(1)] MultiAssetOutput MultiAsset
): Value;
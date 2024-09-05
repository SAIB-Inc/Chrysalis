using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(Lovelace), typeof(LovelaceWithMultiAsset)])]
public record Value : ICbor;

public record Lovelace(ulong Value): CborUlong(Value);

[CborSerializable(CborType.List)]
public record LovelaceWithMultiAsset(
    Lovelace Lovelace,
    MultiAsset MultiAsset
): Value;
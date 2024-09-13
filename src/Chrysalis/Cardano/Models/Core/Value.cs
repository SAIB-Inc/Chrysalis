using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Core.Transaction;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(Lovelace), typeof(LovelaceWithMultiAsset)])]
public record Value : ICbor;

[CborSerializable(CborType.Ulong)]
public record Lovelace(ulong Value): Value;
// @TODO: think about how to support not recreating multiple base types

[CborSerializable(CborType.List)]
public record LovelaceWithMultiAsset(
    Lovelace Lovelace,
    MultiAsset MultiAsset
): Value;
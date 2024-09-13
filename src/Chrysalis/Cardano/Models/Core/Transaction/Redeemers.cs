using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Protocol;
using Chrysalis.Cardano.Models.Core.Script;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(RedeemerList),
    typeof(RedeemerMap)
])]
public record Redeemers : ICbor;

[CborSerializable(CborType.List)]
public record RedeemerList(
    CborDefiniteList<RedeemerEntry> Entries
) : Redeemers;

[CborSerializable(CborType.Map)]
public record RedeemerMap(
    CborMap<RedeemerKey, RedeemerValue> Value
) : Redeemers;

[CborSerializable(CborType.List)]
public record RedeemerEntry(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Index,
    [CborProperty(2)] PlutusData Data,
    [CborProperty(3)] ExUnits ExUnits
) : ICbor;

[CborSerializable(CborType.List)]
public record RedeemerKey(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Index
) : ICbor;

[CborSerializable(CborType.List)]
public record RedeemerValue(
    [CborProperty(0)] PlutusData Data,
    [CborProperty(1)] ExUnits ExUnits
) : ICbor;
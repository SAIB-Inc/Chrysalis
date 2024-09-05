using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(RedeemerList),
    typeof(RedeemerMap)
])]
public record Redeemers : ICbor;

[CborSerializable(CborType.List)]
public record RedeemerList(
    CborIndefiniteList<RedeemerEntry> Entries
) : Redeemers;

[CborSerializable(CborType.Map)]
public record RedeemerMap(
    Dictionary<RedeemerKey, RedeemerValue> Value
) : Redeemers;

[CborSerializable(CborType.List)]
public record RedeemerEntry(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Index,
    [CborProperty(2)] CborBytes Data, //@TODO: Add PlutusData 
    [CborProperty(3)] ExUnits ExUnits
) : ICbor;

[CborSerializable(CborType.List)]
public record RedeemerKey(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Index
) : ICbor;

[CborSerializable(CborType.List)]
public record RedeemerValue(
    [CborProperty(0)] CborBytes Data, //@TODO: Add PlutusData 
    [CborProperty(1)] ExUnits ExUnits
) : ICbor;
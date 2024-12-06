
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Protocol;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;

[CborConverter(typeof(UnionConverter))]
public abstract record Redeemers : CborBase;

[CborConverter(typeof(ListConverter))]
[CborDefinite]
public record RedeemerList(RedeemerEntry[] Value) : Redeemers;

[CborConverter(typeof(MapConverter))]
[CborDefinite]
public record RedeemerMap(Dictionary<RedeemerKey, RedeemerValue> Value) : Redeemers;

[CborConverter(typeof(CustomListConverter))]
public record RedeemerEntry(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Index,
    [CborProperty(2)] PlutusData Data,
    [CborProperty(3)] ExUnits ExUnits
) : CborBase;

[CborConverter(typeof(CustomListConverter))]
public record RedeemerKey(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Index
) : CborBase;

[CborConverter(typeof(CustomListConverter))]
public record RedeemerValue(
    [CborProperty(0)] PlutusData Data,
    [CborProperty(1)] ExUnits ExUnits
) : CborBase;

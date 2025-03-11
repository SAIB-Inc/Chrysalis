using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborConverter(typeof(UnionConverter))]
public abstract partial record Redeemers : CborBase;

[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public partial record RedeemerList(List<RedeemerEntry> Value) : Redeemers;

[CborConverter(typeof(MapConverter))]
[CborOptions(IsDefinite = true)]
public partial record RedeemerMap(Dictionary<RedeemerKey, RedeemerValue> Value) : Redeemers;

[CborConverter(typeof(CustomListConverter))]
public partial record RedeemerEntry(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborUlong Index,
    [CborIndex(2)] PlutusData Data,
    [CborIndex(3)] ExUnits ExUnits
) : CborBase;

[CborConverter(typeof(CustomListConverter))]
public partial record RedeemerKey(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborUlong Index
) : CborBase;

[CborConverter(typeof(CustomListConverter))]
public partial record RedeemerValue(
    [CborIndex(0)] PlutusData Data,
    [CborIndex(1)] ExUnits ExUnits
) : CborBase;

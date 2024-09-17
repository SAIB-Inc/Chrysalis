using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Script;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(PlutusConstr),
    typeof(PlutusMap),
    typeof(PlutusList),
    typeof(PlutusBigInt),
    typeof(PlutusBytes)
])]
public interface PlutusData: ICbor;

public record PlutusConstr(Option<PlutusData> DataConstr): PlutusData;

public record PlutusMap(Dictionary<PlutusData, PlutusData> Value)
    : CborMap<PlutusData, PlutusData>(Value), PlutusData;

public record PlutusBigInt(ulong Value)
    : CborUlong(Value), PlutusData;

public record PlutusBytes(byte[] Value)
    : CborBytes(Value), PlutusData;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(PlutusIndefiniteList),
    typeof(PlutusDefiniteList),
])]
public interface PlutusList: PlutusData;

public record PlutusIndefiniteList(PlutusData[] Value)
    : CborIndefiniteList<PlutusData>(Value), PlutusList;

public record PlutusDefiniteList(PlutusData[] Value)
    : CborIndefiniteList<PlutusData>(Value), PlutusList;
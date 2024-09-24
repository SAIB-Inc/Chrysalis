using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Script;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(PlutusConstr),
    typeof(PlutusMap),
    typeof(PlutusList),
    typeof(PlutusBigInt),
    typeof(PlutusBytes),
    typeof(PlutusBytesWithTag)
])]
public interface PlutusData : ICbor;

[CborSerializable(CborType.Constr)]
public record PlutusConstr(int Index, bool IsInfinite, PlutusData[] Value) : PlutusData;

public record PlutusMap(Dictionary<PlutusData, PlutusData> Value)
    : CborMap<PlutusData, PlutusData>(Value), PlutusData;

public record PlutusBigInt(long Value)
    : CborLong(Value), PlutusData;

public record PlutusBytes(byte[] Value)
    : CborBoundedBytes(Value), PlutusData;

[CborSerializable(CborType.Tag, Index = 2)]
public record PlutusBytesWithTag(PlutusBytes Value) : PlutusData;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(PlutusIndefiniteList),
    typeof(PlutusDefiniteList),
])]
public interface PlutusList : PlutusData;

public record PlutusIndefiniteList(PlutusData[] Value)
    : CborIndefiniteList<PlutusData>(Value), PlutusList;

public record PlutusDefiniteList(PlutusData[] Value)
    : CborDefiniteList<PlutusData>(Value), PlutusList;
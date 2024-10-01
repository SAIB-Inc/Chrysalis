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

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(PlutusBigUInt),
    typeof(PlutusBigNInt),
])]
public interface PlutusBigInt : PlutusData;

public record PlutusBigUInt(ulong Value)
    : CborUlong(Value), PlutusBigInt;

public record PlutusBigNInt(long Value)
    : CborLong(Value), PlutusBigInt;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(PlutusBoundedBytes),
    typeof(PlutusDefiniteBytes),
])]
public interface PlutusBytes : PlutusData;

public record PlutusBoundedBytes(byte[] Value)
    : CborBoundedBytes(Value), PlutusBytes;

public record PlutusDefiniteBytes(byte[] Value)
    : CborBytes(Value), PlutusBytes;

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
using Chrysalis.Cardano.Models.Cbor;
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

public record PlutusData: ICbor;

[CborSerializable(CborType.Constr)]
public record PlutusConstr(PlutusData DataConstr): PlutusData;

public record PlutusMap(CborMap<PlutusData, PlutusData> DataMap): PlutusData;

[CborSerializable(CborType.List)]
public record PlutusList(
    [CborProperty(0)] PlutusData DataList
): PlutusData;

[CborSerializable(CborType.Ulong)]
public record PlutusBigInt(ulong DataInt): PlutusData;

[CborSerializable(CborType.Bytes)]
public record PlutusBytes(CborBytes DataBytes): PlutusData;
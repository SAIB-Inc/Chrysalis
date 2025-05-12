using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public abstract partial record PlutusData : CborBase
{
}

[CborSerializable]
[CborConstr]
public partial record PlutusConstr(CborMaybeIndefList<PlutusData> PlutusData) : PlutusData;

[CborSerializable]
public partial record PlutusMap(Dictionary<PlutusData, PlutusData> PlutusData) : PlutusData;

[CborSerializable]
public partial record PlutusList(List<PlutusData> PlutusData) : PlutusData;


[CborSerializable]
[CborUnion]
public abstract partial record PlutusBigInt : PlutusData
{
}

[CborSerializable]
[CborUnion]
public abstract partial record PlutusInt : PlutusBigInt
{
}

[CborSerializable]
public partial record PlutusInt64(long Value) : PlutusInt;

[CborSerializable]
public partial record PlutusUint64(ulong Value) : PlutusInt;

[CborSerializable]
[CborTag(2)]
public partial record PlutusBigUint([CborSize(64)] byte[] Value) : PlutusBigInt;

[CborSerializable]
[CborTag(3)]
public partial record PlutusBigNint([CborSize(64)] byte[] Value) : PlutusBigInt;

[CborSerializable]
public partial record PlutusBoundedBytes([CborSize(64)] byte[] Value) : PlutusData;
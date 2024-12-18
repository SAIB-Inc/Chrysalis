using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Script;

[CborConverter(typeof(UnionConverter))]
public abstract record PlutusData : CborBase;


[CborConverter(typeof(CustomConstrConverter))]
public record PlutusConstr(List<PlutusData> PlutusData) : PlutusData;


[CborConverter(typeof(MapConverter))]
public record PlutusMap(Dictionary<PlutusData, PlutusData> PlutusData) : PlutusData;

[CborConverter(typeof(ListConverter))]
public record PlutusList(List<PlutusData> PlutusData) : PlutusData;


[CborConverter(typeof(UnionConverter))]
public abstract record PlutusBigInt : PlutusData;


[CborConverter(typeof(UnionConverter))]
public abstract record PlutusInt : PlutusBigInt;


[CborConverter(typeof(LongConverter))]
public record PlutusInt64(long Value) : PlutusInt;


[CborConverter(typeof(UlongConverter))]
public record PlutusUint64(ulong Value) : PlutusInt;


[CborConverter(typeof(BytesConverter))]
[CborSize(64)]
[CborTag(2)]
public record PlutusBigUint(byte[] Value) : PlutusBigInt;


[CborConverter(typeof(BytesConverter))]
[CborSize(64)]
[CborTag(3)]
public record PlutusBigNint(byte[] Value) : PlutusBigInt;


[CborConverter(typeof(BytesConverter))]
[CborSize(64)]
public record PlutusBoundedBytes(byte[] Value) : PlutusData;

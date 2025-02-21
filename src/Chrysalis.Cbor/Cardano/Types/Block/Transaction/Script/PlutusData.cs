using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

[CborConverter(typeof(UnionConverter))]
public abstract record PlutusData : CborBase;


[CborConverter(typeof(CustomConstrConverter))]
[CborOptions(IsDefinite = true)]
public record PlutusConstr(List<PlutusData> PlutusData) : PlutusData;


[CborConverter(typeof(MapConverter))]
[CborOptions(IsDefinite = true)]
public record PlutusMap(Dictionary<PlutusData, PlutusData> PlutusData) : PlutusData;

[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
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
[CborOptions(IsDefinite = true, Size = 64, Tag = 2)]
public record PlutusBigUint(byte[] Value) : PlutusBigInt;


[CborConverter(typeof(BytesConverter))]
[CborOptions(IsDefinite = true, Size = 64, Tag = 3)]
public record PlutusBigNint(byte[] Value) : PlutusBigInt;


[CborConverter(typeof(BytesConverter))]
[CborOptions(IsDefinite = true, Size = 64)]
public record PlutusBoundedBytes(byte[] Value) : PlutusData;

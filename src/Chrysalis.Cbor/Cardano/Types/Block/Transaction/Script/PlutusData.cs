using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

[CborConverter(typeof(UnionConverter))]
public abstract partial record PlutusData : CborBase;


[CborConverter(typeof(CustomConstrConverter))]
[CborOptions(IsDefinite = true)]
public partial record PlutusConstr(List<PlutusData> PlutusData) : PlutusData;


[CborConverter(typeof(MapConverter))]
[CborOptions(IsDefinite = true)]
public partial record PlutusMap(Dictionary<PlutusData, PlutusData> PlutusData) : PlutusData;

[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public partial record PlutusList(List<PlutusData> PlutusData) : PlutusData;


[CborConverter(typeof(UnionConverter))]
public abstract partial record PlutusBigInt : PlutusData;


[CborConverter(typeof(UnionConverter))]
public abstract partial record PlutusInt : PlutusBigInt;


[CborConverter(typeof(LongConverter))]
public partial record PlutusInt64(long Value) : PlutusInt;


[CborConverter(typeof(UlongConverter))]
public partial record PlutusUint64(ulong Value) : PlutusInt;


[CborConverter(typeof(BytesConverter))]
[CborOptions(IsDefinite = true, Size = 64, Tag = 2)]
public partial record PlutusBigUint(byte[] Value) : PlutusBigInt;


[CborConverter(typeof(BytesConverter))]
[CborOptions(IsDefinite = true, Size = 64, Tag = 3)]
public partial record PlutusBigNint(byte[] Value) : PlutusBigInt;


[CborConverter(typeof(BytesConverter))]
[CborOptions(IsDefinite = true, Size = 64)]
public partial record PlutusBoundedBytes(byte[] Value) : PlutusData;

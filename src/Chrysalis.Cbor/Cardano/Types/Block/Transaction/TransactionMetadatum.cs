using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

[CborConverter(typeof(UnionConverter))]
public abstract partial record TransactionMetadatum : CborBase;


[CborConverter(typeof(MapConverter))]
public partial record MetadatumMap(
    Dictionary<TransactionMetadatum, TransactionMetadatum> Value
) : TransactionMetadatum;


[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public partial record MetadatumList(
    List<TransactionMetadatum> Value
) : TransactionMetadatum;


[CborConverter(typeof(BytesConverter))]
public partial record MetadatumBytes(byte[] Value) : TransactionMetadatum;


[CborConverter(typeof(TextConverter))]
public partial record MetadataText(string Value) : TransactionMetadatum;


[CborConverter(typeof(UnionConverter))]
public abstract partial record MetadatumInt : TransactionMetadatum;


[CborConverter(typeof(LongConverter))]
public partial record MetadatumIntLong(long Value) : MetadatumInt;


[CborConverter(typeof(UlongConverter))]
public partial record MetadatumIntUlong(ulong Value) : MetadatumInt;
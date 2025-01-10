using Chrysalis.Cbor.Abstractions;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction;

[CborConverter(typeof(UnionConverter))]
public abstract record TransactionMetadatum : CborBase;


[CborConverter(typeof(MapConverter))]
public record MetadatumMap(
    Dictionary<TransactionMetadatum, TransactionMetadatum> Value
) : TransactionMetadatum;


[CborConverter(typeof(ListConverter))]
[CborDefinite]
public record MetadatumList(
    List<TransactionMetadatum> Value
) : TransactionMetadatum;


[CborConverter(typeof(BytesConverter))]
public record MetadatumBytes(ReadOnlyMemory<byte> Value) : TransactionMetadatum;


[CborConverter(typeof(TextConverter))]
public record MetadataText(string Value) : TransactionMetadatum;


[CborConverter(typeof(UnionConverter))]
public abstract record MetadatumInt : TransactionMetadatum;


[CborConverter(typeof(LongConverter))]
public record MetadatumIntLong(long Value) : MetadatumInt;


[CborConverter(typeof(UlongConverter))]
public record MetadatumIntUlong(ulong Value) : MetadatumInt;
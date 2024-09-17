using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(MetadatumMap),
    typeof(MetadatumList),
    typeof(MetadatumInt),
    typeof(MetadatumBytes),
    typeof(MetadataText)
])]
public interface TransactionMetadatum : ICbor;

public record MetadatumMap(Dictionary<TransactionMetadatum, TransactionMetadatum> Value)
    : CborMap<TransactionMetadatum, TransactionMetadatum>(Value), TransactionMetadatum;

public record MetadatumList(TransactionMetadatum[] Value)
    : CborDefiniteList<TransactionMetadatum>(Value), TransactionMetadatum;

public record MetadatumInt(ulong Value)
    : CborUlong(Value), TransactionMetadatum;

public record MetadatumBytes(byte[] Value)
    : CborBytes(Value), TransactionMetadatum;

public record MetadataText(string Value)
    : CborText(Value), TransactionMetadatum;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(Map),
    typeof(Array),
    typeof(Int),
    typeof(Bytes),
    typeof(Text),
])]

public record TransactionMetadatum: ICbor;

public record Map(
    CborMap<TransactionMetadatum, TransactionMetadatum> TransactionMetadatum
) : TransactionMetadatum;

[CborSerializable(CborType.List)]
public record Array(
    [CborProperty(0)] TransactionMetadatum TransactionMetadatum
): TransactionMetadatum;

[CborSerializable(CborType.Int)]
public record Int(CborInt Value): TransactionMetadatum;

[CborSerializable(CborType.Bytes)]
public record Bytes(CborBytes Value): TransactionMetadatum;

[CborSerializable(CborType.Text)]
public record Text(CborText Value): TransactionMetadatum;
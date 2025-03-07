using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

[CborConverter(typeof(UnionConverter))]
public abstract record Transaction : CborBase;


[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ShelleyTransaction(
    [CborIndex(0)] TransactionBody TransactionBody,
    [CborIndex(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborIndex(2)] CborNullable<Metadata> TransactionMetadata
) : Transaction;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record AllegraTransaction(
    [CborIndex(0)] TransactionBody TransactionBody,
    [CborIndex(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborIndex(2)] CborNullable<AuxiliaryData> AuxiliaryData
) : Transaction;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record PostMaryTransaction(
    [CborIndex(0)] TransactionBody TransactionBody,
    [CborIndex(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborIndex(2)] CborBool IsValid,
    [CborIndex(3)] CborNullable<AuxiliaryData> AuxiliaryData
) : Transaction;


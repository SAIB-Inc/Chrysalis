using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction;

[CborConverter(typeof(UnionConverter))]
public abstract record Transaction : CborBase;


[CborConverter(typeof(CustomListConverter))]
public record ShelleyTransaction(
    [CborProperty(0)] TransactionBody TransactionBody,
    [CborProperty(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborProperty(2)] CborNullable<Metadata> TransactionMetadata
) : Transaction;

[CborConverter(typeof(CustomListConverter))]
public record AllegraTransaction(
    [CborProperty(0)] TransactionBody TransactionBody,
    [CborProperty(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborProperty(2)] CborNullable<AuxiliaryData> AuxiliaryData
) : Transaction;

[CborConverter(typeof(CustomListConverter))]
public record PostMaryTransaction(
    [CborProperty(0)] TransactionBody TransactionBody,
    [CborProperty(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborProperty(2)] CborBool IsValid,
    [CborProperty(3)] CborNullable<AuxiliaryData> AuxiliaryData
) : Transaction;


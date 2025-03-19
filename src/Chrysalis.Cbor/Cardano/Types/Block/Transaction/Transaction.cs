using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

[CborSerializable]
[CborUnion]
public abstract partial record Transaction : CborBase<Transaction>
{
}

[CborSerializable]
[CborList]
public partial record ShelleyTransaction(
       [CborOrder(0)] TransactionBody TransactionBody,
       [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
       [CborOrder(2)][CborNullable] Metadata? TransactionMetadata
   ) : Transaction;

[CborSerializable]
[CborList]
public partial record AllegraTransaction(
    [CborOrder(0)] TransactionBody TransactionBody,
    [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborOrder(2)][CborNullable] AuxiliaryData? AuxiliaryData
) : Transaction;

[CborSerializable]
[CborList]
public partial record PostMaryTransaction(
    [CborOrder(0)] TransactionBody TransactionBody,
    [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborOrder(2)] bool IsValid,
    [CborOrder(3)][CborNullable] AuxiliaryData? AuxiliaryData
) : Transaction;


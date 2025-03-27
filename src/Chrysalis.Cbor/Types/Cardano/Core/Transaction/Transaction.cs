using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborUnion]
public abstract partial record Transaction : CborBase
{
}

[CborSerializable]
[CborList]
public partial record ShelleyTransaction(
   [CborOrder(0)] TransactionBody TransactionBody,
   [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
   [CborOrder(2)][CborNullable] Metadata? TransactionMetadata
) : Transaction, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record AllegraTransaction(
    [CborOrder(0)] TransactionBody TransactionBody,
    [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborOrder(2)][CborNullable] AuxiliaryData? AuxiliaryData
) : Transaction, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record PostMaryTransaction(
    [CborOrder(0)] TransactionBody TransactionBody,
    [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborOrder(2)] bool IsValid,
    [CborOrder(3)][CborNullable] AuxiliaryData? AuxiliaryData
) : Transaction, ICborPreserveRaw;

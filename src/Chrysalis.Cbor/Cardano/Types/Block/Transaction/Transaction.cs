using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction;

[CborSerializable]
[CborUnion]
public abstract partial record Transaction : CborBase<Transaction>
{
    [CborSerializable]
    [CborList]
    public partial record ShelleyTransaction(
        [CborIndex(0)] TransactionBody TransactionBody,
        [CborIndex(1)] TransactionWitnessSet TransactionWitnessSet,
        [CborIndex(2)][CborNullable] Metadata? TransactionMetadata
    ) : Transaction;

    [CborSerializable]
    [CborList]
    public partial record AllegraTransaction(
        [CborIndex(0)] TransactionBody TransactionBody,
        [CborIndex(1)] TransactionWitnessSet TransactionWitnessSet,
        [CborIndex(2)][CborNullable] AuxiliaryData? AuxiliaryData
    ) : Transaction;

    [CborSerializable]
    [CborList]
    public partial record PostMaryTransaction(
        [CborIndex(0)] TransactionBody TransactionBody,
        [CborIndex(1)] TransactionWitnessSet TransactionWitnessSet,
        [CborIndex(2)] bool IsValid,
        [CborIndex(3)][CborNullable] AuxiliaryData? AuxiliaryData
    ) : Transaction;
}


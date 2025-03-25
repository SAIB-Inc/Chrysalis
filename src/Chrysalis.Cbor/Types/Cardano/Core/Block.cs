using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Types.Cardano.Core;

[CborSerializable]
[CborUnion]
public abstract partial record Block : CborBase { }

[CborSerializable]
[CborList]
public partial record AlonzoCompatibleBlock(
[CborOrder(0)] BlockHeader Header,
[CborOrder(1)] CborMaybeIndefList<AlonzoTransactionBody> TransactionBodies,
[CborOrder(2)] CborMaybeIndefList<AlonzoTransactionWitnessSet> TransactionWitnessSets,
[CborOrder(3)] AuxiliaryDataSet AuxiliaryDataSet,
[CborOrder(4)] CborMaybeIndefList<int>? InvalidTransactions
) : Block, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record BabbageBlock(
    [CborOrder(0)] BlockHeader Header,
    [CborOrder(1)] CborMaybeIndefList<BabbageTransactionBody> TransactionBodies,
    [CborOrder(2)] CborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets,
    [CborOrder(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborOrder(4)] CborMaybeIndefList<int>? InvalidTransactions
) : Block, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record ConwayBlock(
    [CborOrder(0)] BlockHeader Header,
    [CborOrder(1)] CborMaybeIndefList<ConwayTransactionBody> TransactionBodies,
    [CborOrder(2)] CborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets,
    [CborOrder(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborOrder(4)] CborMaybeIndefList<int>? InvalidTransactions
) : Block, ICborPreserveRaw;

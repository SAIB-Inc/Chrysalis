using Chrysalis.Cbor.Cardano.Types.Block.Header;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Cardano.Types.Block;

[CborSerializable]
[CborUnion]
public abstract partial record Block : CborBase<Block> { }

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

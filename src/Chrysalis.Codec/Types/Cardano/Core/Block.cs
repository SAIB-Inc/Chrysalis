using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Codec.Types.Cardano.Core;

[CborSerializable]
[CborUnion]
public partial interface IBlock : ICborType;

[CborSerializable]
[CborList]
public readonly partial record struct AlonzoCompatibleBlock : IBlock
{
    [CborOrder(0)] public partial BlockHeader Header { get; }
    [CborOrder(1)] public partial ICborMaybeIndefList<AlonzoTransactionBody> TransactionBodies { get; }
    [CborOrder(2)] public partial ICborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets { get; }
    [CborOrder(3)] public partial AuxiliaryDataSet AuxiliaryDataSet { get; }
    [CborOrder(4)] public partial ICborMaybeIndefList<int>? InvalidTransactions { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct BabbageBlock : IBlock
{
    [CborOrder(0)] public partial BlockHeader Header { get; }
    [CborOrder(1)] public partial ICborMaybeIndefList<BabbageTransactionBody> TransactionBodies { get; }
    [CborOrder(2)] public partial ICborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets { get; }
    [CborOrder(3)] public partial AuxiliaryDataSet AuxiliaryDataSet { get; }
    [CborOrder(4)] public partial ICborMaybeIndefList<int>? InvalidTransactions { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ConwayBlock : IBlock
{
    [CborOrder(0)] public partial BlockHeader Header { get; }
    [CborOrder(1)] public partial ICborMaybeIndefList<ConwayTransactionBody> TransactionBodies { get; }
    [CborOrder(2)] public partial ICborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets { get; }
    [CborOrder(3)] public partial AuxiliaryDataSet AuxiliaryDataSet { get; }
    [CborOrder(4)] public partial ICborMaybeIndefList<int>? InvalidTransactions { get; }
}

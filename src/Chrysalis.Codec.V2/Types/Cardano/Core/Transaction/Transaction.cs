using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborUnion]
public partial interface ITransaction : ICborType;

[CborSerializable]
[CborList]
public readonly partial record struct ShelleyTransaction : ITransaction
{
    [CborOrder(0)] public partial ITransactionBody Body { get; }
    [CborOrder(1)] public partial ITransactionWitnessSet Witnesses { get; }
    [CborOrder(2)] public partial Metadata? Metadata { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct AllegraTransaction : ITransaction
{
    [CborOrder(0)] public partial ITransactionBody Body { get; }
    [CborOrder(1)] public partial ITransactionWitnessSet Witnesses { get; }
    [CborOrder(2)] public partial IAuxiliaryData? AuxiliaryData { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct PostMaryTransaction : ITransaction
{
    [CborOrder(0)] public partial ITransactionBody Body { get; }
    [CborOrder(1)] public partial ITransactionWitnessSet Witnesses { get; }
    [CborOrder(2)] public partial bool IsValid { get; }
    [CborOrder(3)] public partial IAuxiliaryData? AuxiliaryData { get; }
}

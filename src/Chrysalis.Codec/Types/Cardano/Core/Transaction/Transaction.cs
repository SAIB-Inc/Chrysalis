using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Codec.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborUnion]
public partial interface ITransaction : ICborType
{
    /// <summary>Decodes a stand-alone transaction from the specified era.</summary>
    static ITransaction Read(ReadOnlyMemory<byte> data, Era era) => Read(data, era, out _);

    /// <summary>Decodes a stand-alone transaction and reports the number of bytes consumed.</summary>
    static ITransaction Read(ReadOnlyMemory<byte> data, Era era, out int bytesConsumed)
    {
        try
        {
            return TransactionEraReader.Read(data, era, out bytesConsumed);
        }
        catch (Exception error) when (error is not CborDecodeException && CborError.IsDecodeFailure(error))
        {
            throw CborError.Wrap(error, data, nameof(ITransaction), "transaction envelope");
        }
    }
}

[CborSerializable]
[CborList]
[CborDefinite(4)]
public readonly partial record struct PostMaryTransaction : ITransaction
{
    [CborOrder(0)] public partial ITransactionBody Body { get; }
    [CborOrder(1)] public partial ITransactionWitnessSet Witnesses { get; }
    [CborOrder(2)] public partial bool IsValid { get; }
    [CborOrder(3)] public partial IAuxiliaryData? AuxiliaryData { get; }
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
public readonly partial record struct ShelleyTransaction : ITransaction
{
    [CborOrder(0)] public partial ITransactionBody Body { get; }
    [CborOrder(1)] public partial ITransactionWitnessSet Witnesses { get; }
    [CborOrder(2)] public partial Metadata? Metadata { get; }
}

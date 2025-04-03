using CBlock = Chrysalis.Cbor.Types.Cardano.Core.Block;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core;

public static class BlockExtensions
{
    public static BlockHeader Header(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.Header,
            BabbageBlock babbageBlock => babbageBlock.Header,
            ConwayBlock conwayBlock => conwayBlock.Header,
            _ => throw new NotSupportedException()
        };

    public static string Hash(this BlockHeader self) => Convert.ToHexString(Blake2Fast.Blake2b.ComputeHash(32, self.Raw!.Value.Span)).ToLowerInvariant();

    public static IEnumerable<TransactionBody> TransactionBodies(this CBlock self)
    {
        return self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionBodies.GetValue(),
            BabbageBlock babbageBlock => babbageBlock.TransactionBodies.GetValue(),
            ConwayBlock conwayBlock => conwayBlock.TransactionBodies.GetValue(),
            _ => []
        };
    }

    public static IEnumerable<TransactionWitnessSet> TransactionWitnessSets(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionWitnessSets.GetValue(),
            BabbageBlock babbageBlock => babbageBlock.TransactionWitnessSets.GetValue(),
            ConwayBlock conwayBlock => conwayBlock.TransactionWitnessSets.GetValue(),
            _ => []
        };

    public static Dictionary<int, AuxiliaryData> AuxiliaryDataSet(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.AuxiliaryDataSet.Value,
            BabbageBlock babbageBlock => babbageBlock.AuxiliaryDataSet.Value,
            ConwayBlock conwayBlock => conwayBlock.AuxiliaryDataSet.Value,
            _ => []
        };

    public static IEnumerable<int>? InvalidTransactions(this CBlock self) =>
        self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.InvalidTransactions?.GetValue(),
            BabbageBlock babbageBlock => babbageBlock.InvalidTransactions?.GetValue(),
            ConwayBlock conwayBlock => conwayBlock.InvalidTransactions?.GetValue(),
            _ => null
        };
}
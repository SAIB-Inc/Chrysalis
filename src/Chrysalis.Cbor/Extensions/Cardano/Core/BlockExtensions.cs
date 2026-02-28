using CBlock = Chrysalis.Cbor.Types.Cardano.Core.Block;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core;

/// <summary>
/// Extension methods for <see cref="CBlock"/> to access block components across eras.
/// </summary>
public static class BlockExtensions
{
    /// <summary>
    /// Gets the block header from the block.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The block header.</returns>
    public static BlockHeader Header(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.Header,
            BabbageBlock babbageBlock => babbageBlock.Header,
            ConwayBlock conwayBlock => conwayBlock.Header,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Computes the Blake2b-256 hash of the block header as a hex string.
    /// </summary>
    /// <param name="self">The block header instance.</param>
    /// <returns>The hex-encoded hash string.</returns>
    public static string Hash(this BlockHeader self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return Convert.ToHexString(Blake2Fast.Blake2b.HashData(32, self.Raw!.Value.Span)).ToUpperInvariant();
    }

    /// <summary>
    /// Gets the transaction bodies from the block.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The transaction bodies.</returns>
    public static IEnumerable<TransactionBody> TransactionBodies(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionBodies.GetValue(),
            BabbageBlock babbageBlock => babbageBlock.TransactionBodies.GetValue(),
            ConwayBlock conwayBlock => conwayBlock.TransactionBodies.GetValue(),
            _ => []
        };
    }

    /// <summary>
    /// Gets the transaction witness sets from the block.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The transaction witness sets.</returns>
    public static IEnumerable<TransactionWitnessSet> TransactionWitnessSets(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionWitnessSets.GetValue(),
            BabbageBlock babbageBlock => babbageBlock.TransactionWitnessSets.GetValue(),
            ConwayBlock conwayBlock => conwayBlock.TransactionWitnessSets.GetValue(),
            _ => []
        };
    }

    /// <summary>
    /// Gets the auxiliary data set mapping transaction indices to auxiliary data.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The auxiliary data set dictionary.</returns>
    public static Dictionary<int, AuxiliaryData> AuxiliaryDataSet(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.AuxiliaryDataSet.Value,
            BabbageBlock babbageBlock => babbageBlock.AuxiliaryDataSet.Value,
            ConwayBlock conwayBlock => conwayBlock.AuxiliaryDataSet.Value,
            _ => []
        };
    }

    /// <summary>
    /// Gets the indices of invalid transactions in the block, if any.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The invalid transaction indices, or null.</returns>
    public static IEnumerable<int>? InvalidTransactions(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.InvalidTransactions?.GetValue(),
            BabbageBlock babbageBlock => babbageBlock.InvalidTransactions?.GetValue(),
            ConwayBlock conwayBlock => conwayBlock.InvalidTransactions?.GetValue(),
            _ => null
        };
    }
}

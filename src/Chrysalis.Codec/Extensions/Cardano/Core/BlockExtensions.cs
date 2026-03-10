using CBlock = Chrysalis.Codec.Types.Cardano.Core.IBlock;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Byron;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Codec.Extensions.Cardano.Core;

/// <summary>
/// Extension methods for <see cref="CBlock"/> to access block components across eras.
/// </summary>
public static class BlockExtensions
{
    /// <summary>
    /// Gets the slot number from any block era.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The absolute slot number.</returns>
    public static ulong Slot(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ByronMainBlock byron => (byron.Header.ConsensusData.SlotId.Epoch * 21600) + byron.Header.ConsensusData.SlotId.Slot,
            ByronEbBlock ebb => ebb.Header.ConsensusData.Epoch * 21600,
            _ => self.Header().HeaderBody.Slot()
        };
    }

    /// <summary>
    /// Gets the block height (block number) from any block era.
    /// Byron EBBs return 0 as they don't have block numbers.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The block height.</returns>
    public static ulong Height(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ByronMainBlock byron => byron.Header.ConsensusData.Difficulty.GetValue().FirstOrDefault(),
            ByronEbBlock => 0,
            _ => self.Header().HeaderBody.BlockNumber()
        };
    }

    /// <summary>
    /// Computes the Blake2b-256 hash of the block as a lowercase hex string.
    /// For post-Byron blocks, hashes the block header. For Byron blocks, hashes the raw header.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The hex-encoded hash string.</returns>
    public static string Hash(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ByronMainBlock byron => Convert.ToHexStringLower(HashByronHeader(1, byron.Header.Raw.Span)),
            ByronEbBlock ebb => Convert.ToHexStringLower(HashByronHeader(0, ebb.Header.Raw.Span)),
            _ => self.Header().Hash()
        };
    }

    /// <summary>
    /// Gets the era from any block type.
    /// Note: <see cref="AlonzoCompatibleBlock"/> covers Shelley through Alonzo and returns <see cref="Era.Alonzo"/>.
    /// Use <see cref="Era(BlockWithEra)"/> for precise era identification.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The era.</returns>
    public static Era Era(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ByronMainBlock or ByronEbBlock => Types.Cardano.Core.Era.Byron,
            AlonzoCompatibleBlock => Types.Cardano.Core.Era.Alonzo,
            BabbageBlock => Types.Cardano.Core.Era.Babbage,
            ConwayBlock => Types.Cardano.Core.Era.Conway,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Gets the precise era from a <see cref="BlockWithEra"/> using its era number tag.
    /// </summary>
    /// <param name="self">The block with era instance.</param>
    /// <returns>The precise era.</returns>
    public static Era Era(this BlockWithEra self)
    {
        return self.EraNumber switch
        {
            0 or 1 => Types.Cardano.Core.Era.Byron,
            2 => Types.Cardano.Core.Era.Shelley,
            3 => Types.Cardano.Core.Era.Allegra,
            4 => Types.Cardano.Core.Era.Mary,
            5 => Types.Cardano.Core.Era.Alonzo,
            6 => Types.Cardano.Core.Era.Babbage,
            7 => Types.Cardano.Core.Era.Conway,
            _ => throw new NotSupportedException($"Unknown era number: {self.EraNumber}")
        };
    }

    /// <summary>
    /// Gets the block header from the block. Only supported for post-Byron eras.
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
        return Convert.ToHexStringLower(Blake2Fast.Blake2b.HashData(32, self.Raw.Span));
    }

    /// <summary>
    /// Computes the Byron block hash by wrapping the header in a CBOR tuple [tag, header_bytes] before hashing.
    /// Byron EBB uses tag=0, Byron main block uses tag=1.
    /// </summary>
    /// <param name="tag">The Byron block type tag (0 = EBB, 1 = main block).</param>
    /// <param name="headerCbor">The raw CBOR bytes of the Byron header.</param>
    /// <returns>The Blake2b-256 hash of the wrapped header.</returns>
    public static byte[] HashByronHeader(byte tag, ReadOnlySpan<byte> headerCbor)
    {
        // Byron block hash = Blake2b-256(CBOR-encode([tag, header]))
        // where tag=0 for EBB, tag=1 for main block
        byte[] wrapped = new byte[2 + headerCbor.Length];
        wrapped[0] = 0x82; // CBOR array(2)
        wrapped[1] = tag;  // CBOR uint(0) or uint(1)
        headerCbor.CopyTo(wrapped.AsSpan(2));
        return Blake2Fast.Blake2b.HashData(32, wrapped);
    }

    /// <summary>
    /// Gets the transaction bodies from the block.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The transaction bodies.</returns>
    public static IEnumerable<ITransactionBody> TransactionBodies(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ByronMainBlock byron => byron.Body.TxPayload.GetValue()
                .Select(payload => (ITransactionBody)new ByronTransactionBodyAdapter(payload)),
            ByronEbBlock => [],
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionBodies.GetValue().Cast<ITransactionBody>(),
            BabbageBlock babbageBlock => babbageBlock.TransactionBodies.GetValue().Cast<ITransactionBody>(),
            ConwayBlock conwayBlock => conwayBlock.TransactionBodies.GetValue().Cast<ITransactionBody>(),
            _ => []
        };
    }

    /// <summary>
    /// Gets the transaction witness sets from the block.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The transaction witness sets.</returns>
    public static IEnumerable<ITransactionWitnessSet> TransactionWitnessSets(this CBlock self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoCompatibleBlock alonzoCompatibleBlock => alonzoCompatibleBlock.TransactionWitnessSets.GetValue().Cast<ITransactionWitnessSet>(),
            BabbageBlock babbageBlock => babbageBlock.TransactionWitnessSets.GetValue().Cast<ITransactionWitnessSet>(),
            ConwayBlock conwayBlock => conwayBlock.TransactionWitnessSets.GetValue().Cast<ITransactionWitnessSet>(),
            _ => []
        };
    }

    /// <summary>
    /// Gets the auxiliary data set mapping transaction indices to auxiliary data.
    /// </summary>
    /// <param name="self">The block instance.</param>
    /// <returns>The auxiliary data set dictionary.</returns>
    public static Dictionary<int, IAuxiliaryData> AuxiliaryDataSet(this CBlock self)
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

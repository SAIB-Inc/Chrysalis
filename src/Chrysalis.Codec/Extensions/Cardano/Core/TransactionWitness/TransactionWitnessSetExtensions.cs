using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using CBootstrapWitness = Chrysalis.Codec.Types.Cardano.Core.TransactionWitness.BootstrapWitness;
using CVKeyWitness = Chrysalis.Codec.Types.Cardano.Core.TransactionWitness.VKeyWitness;

namespace Chrysalis.Codec.Extensions.Cardano.Core.TransactionWitness;

/// <summary>
/// Extension methods for <see cref="ITransactionWitnessSet"/> to access witness components.
/// </summary>
public static class TransactionWitnessSetExtensions
{
    /// <summary>
    /// Gets the VKey witness set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The VKey witnesses, or null.</returns>
    public static IEnumerable<CVKeyWitness>? VKeyWitnessSet(this ITransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.VKeyWitnesses,
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.VKeyWitnesses,
            _ => null
        };
    }

    /// <summary>
    /// Gets the native script set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The native scripts, or null.</returns>
    public static IEnumerable<INativeScript>? NativeScriptSet(this ITransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.NativeScripts,
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.NativeScripts,
            _ => null
        };
    }

    /// <summary>
    /// Gets the bootstrap witness set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The bootstrap witnesses, or null.</returns>
    public static IEnumerable<CBootstrapWitness>? BootstrapWitnessSet(this ITransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.BootstrapWitnesses,
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.BootstrapWitnesses,
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus V1 script set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The Plutus V1 scripts, or null.</returns>
    public static IEnumerable<ReadOnlyMemory<byte>>? PlutusV1ScriptSet(this ITransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusV1Scripts,
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV1Scripts,
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus data set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The Plutus data items, or null.</returns>
    public static IEnumerable<IPlutusData>? PlutusDataSet(this ITransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusDataSet,
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusDataSet,
            _ => null
        };
    }

    /// <summary>
    /// Gets the redeemers, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The redeemers, or null.</returns>
    public static IRedeemers? Redeemers(this ITransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.Redeemers,
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.Redeemers,
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus V2 script set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The Plutus V2 scripts, or null.</returns>
    public static IEnumerable<ReadOnlyMemory<byte>>? PlutusV2ScriptSet(this ITransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV2Scripts,
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus V3 script set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The Plutus V3 scripts, or null.</returns>
    public static IEnumerable<ReadOnlyMemory<byte>>? PlutusV3ScriptSet(this ITransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV3Scripts,
            _ => null
        };
    }
}

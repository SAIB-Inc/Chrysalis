using Chrysalis.Codec.V2.Types.Cardano.Core.Common;
using Chrysalis.Codec.V2.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.V2.Types.Cardano.Core.TransactionWitness;
using CBootstrapWitness = Chrysalis.Codec.V2.Types.Cardano.Core.TransactionWitness.BootstrapWitness;
using CVKeyWitness = Chrysalis.Codec.V2.Types.Cardano.Core.TransactionWitness.VKeyWitness;

namespace Chrysalis.Codec.V2.Extensions.Cardano.Core.TransactionWitness;

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
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.VKeyWitnesses?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.VKeyWitnesses?.GetValue(),
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
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.NativeScripts?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.NativeScripts?.GetValue(),
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
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.BootstrapWitnesses?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.BootstrapWitnesses?.GetValue(),
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
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusV1Scripts?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV1Scripts?.GetValue(),
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
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusDataSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusDataSet?.GetValue(),
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
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV2Scripts?.GetValue(),
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
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV3Scripts?.GetValue(),
            _ => null
        };
    }
}

using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using CBootstrapWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.BootstrapWitness;
using CVKeyWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.VKeyWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;

/// <summary>
/// Extension methods for <see cref="TransactionWitnessSet"/> to access witness components.
/// </summary>
public static class TransactionWitnessSetExtensions
{
    /// <summary>
    /// Gets the VKey witness set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The VKey witnesses, or null.</returns>
    public static IEnumerable<CVKeyWitness>? VKeyWitnessSet(this TransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.VKeyWitnessSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.VKeyWitnessSet?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the native script set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The native scripts, or null.</returns>
    public static IEnumerable<NativeScript>? NativeScriptSet(this TransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.NativeScriptSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.NativeScriptSet?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the bootstrap witness set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The bootstrap witnesses, or null.</returns>
    public static IEnumerable<CBootstrapWitness>? BootstrapWitnessSet(this TransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.BootstrapWitnessSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.BootstrapWitnessSet?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus V1 script set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The Plutus V1 scripts, or null.</returns>
    public static IEnumerable<ReadOnlyMemory<byte>>? PlutusV1ScriptSet(this TransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusV1ScriptSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV1ScriptSet?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus data set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The Plutus data items, or null.</returns>
    public static IEnumerable<PlutusData>? PlutusDataSet(this TransactionWitnessSet self)
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
    public static Redeemers? Redeemers(this TransactionWitnessSet self)
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
    public static IEnumerable<ReadOnlyMemory<byte>>? PlutusV2ScriptSet(this TransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV2ScriptSet?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus V3 script set, if present.
    /// </summary>
    /// <param name="self">The transaction witness set instance.</param>
    /// <returns>The Plutus V3 scripts, or null.</returns>
    public static IEnumerable<ReadOnlyMemory<byte>>? PlutusV3ScriptSet(this TransactionWitnessSet self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV3ScriptSet?.GetValue(),
            _ => null
        };
    }
}

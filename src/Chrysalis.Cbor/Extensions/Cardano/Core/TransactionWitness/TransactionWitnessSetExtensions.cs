using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using CBootstrapWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.BootstrapWitness;
using CVKeyWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.VKeyWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;

public static class TransactionWitnessSetExtensions
{
    public static IEnumerable<CVKeyWitness>? VKeyWitnessSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.VKeyWitnessSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.VKeyWitnessSet?.GetValue(),
            _ => null
        };
    
    public static IEnumerable<NativeScript>? NativeScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.NativeScriptSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.NativeScriptSet?.GetValue(),
            _ => null
        };

    public static IEnumerable<CBootstrapWitness>? BootstrapWitnessSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.BootstrapWitnessSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.BootstrapWitnessSet?.GetValue(),
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV1ScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusV1ScriptSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV1ScriptSet?.GetValue(),
            _ => null
        };

    public static IEnumerable<PlutusData>? PlutusDataSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusDataSet?.GetValue(),
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusDataSet?.GetValue(),
            _ => null
        };

    public static Redeemers? Redeemers(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.Redeemers,
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.Redeemers,
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV2ScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV2ScriptSet?.GetValue(),
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV3ScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV3ScriptSet?.GetValue(),
            _ => null
        };
}

using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using CNativeScript = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script.NativeScript;
using CVKeyWitness = Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.VKeyWitness;
using CBootstrapWitness = Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.BootstrapWitness;
using Chrysalis.Cbor.Types.Custom;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.TransactionWitnessSet;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.WitnessSet;

public static class TransactionWitnessSetExtensions
{
    public static IEnumerable<CVKeyWitness>? VKeyWitnessSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.VKeyWitnessSet switch
            {
                CborMaybeIndefList<CVKeyWitness>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CVKeyWitness>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CVKeyWitness>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CVKeyWitness>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.VKeyWitnessSet switch
            {
                CborMaybeIndefList<CVKeyWitness>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CVKeyWitness>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CVKeyWitness>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CVKeyWitness>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };
    
    public static IEnumerable<CNativeScript>? NativeScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.NativeScriptSet switch
            {
                CborMaybeIndefList<CNativeScript>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CNativeScript>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CNativeScript>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CNativeScript>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.NativeScriptSet switch
            {
                CborMaybeIndefList<CNativeScript>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CNativeScript>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CNativeScript>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CNativeScript>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<CBootstrapWitness>? BootstrapWitnessSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.BootstrapWitnessSet switch
            {
                CborMaybeIndefList<CBootstrapWitness>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CBootstrapWitness>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CBootstrapWitness>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CBootstrapWitness>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.BootstrapWitnessSet switch
            {
                CborMaybeIndefList<CBootstrapWitness>.CborDefList defList => defList.Value,
                CborMaybeIndefList<CBootstrapWitness>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<CBootstrapWitness>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<CBootstrapWitness>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV1ScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusV1ScriptSet switch
            {
                CborMaybeIndefList<byte[]>.CborDefList defList => defList.Value,
                CborMaybeIndefList<byte[]>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<byte[]>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<byte[]>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV1ScriptSet switch
            {
                CborMaybeIndefList<byte[]>.CborDefList defList => defList.Value,
                CborMaybeIndefList<byte[]>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<byte[]>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<byte[]>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<PlutusData>? PlutusDataSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusDataSet switch
            {
                CborMaybeIndefList<PlutusData>.CborDefList defList => defList.Value,
                CborMaybeIndefList<PlutusData>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<PlutusData>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<PlutusData>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusDataSet switch
            {
                CborMaybeIndefList<PlutusData>.CborDefList defList => defList.Value,
                CborMaybeIndefList<PlutusData>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<PlutusData>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<PlutusData>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
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
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV2ScriptSet switch
            {
                CborMaybeIndefList<byte[]>.CborDefList defList => defList.Value,
                CborMaybeIndefList<byte[]>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<byte[]>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<byte[]>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV3ScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV3ScriptSet switch
            {
                CborMaybeIndefList<byte[]>.CborDefList defList => defList.Value,
                CborMaybeIndefList<byte[]>.CborIndefList indefList => indefList.Value,
                CborMaybeIndefList<byte[]>.CborDefListWithTag defListWithTag => defListWithTag.Value,
                CborMaybeIndefList<byte[]>.CborIndefListWithTag indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };
}
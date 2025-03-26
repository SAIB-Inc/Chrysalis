using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using CBootstrapWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.BootstrapWitness;
using CVKeyWitness = Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness.VKeyWitness;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.WitnessSet;

public static class TransactionWitnessSetExtensions
{
    public static IEnumerable<CVKeyWitness>? VKeyWitnessSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.VKeyWitnessSet switch
            {
                CborDefList<CVKeyWitness> defList => defList.Value,
                CborIndefList<CVKeyWitness> indefList => indefList.Value,
                CborDefListWithTag<CVKeyWitness> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<CVKeyWitness> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.VKeyWitnessSet switch
            {
                CborDefList<CVKeyWitness> defList => defList.Value,
                CborIndefList<CVKeyWitness> indefList => indefList.Value,
                CborDefListWithTag<CVKeyWitness> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<CVKeyWitness> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };
    
    public static IEnumerable<NativeScript>? NativeScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.NativeScriptSet switch
            {
                CborDefList<NativeScript> defList => defList.Value,
                CborIndefList<NativeScript> indefList => indefList.Value,
                CborDefListWithTag<NativeScript> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<NativeScript> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.NativeScriptSet switch
            {
                CborDefList<NativeScript> defList => defList.Value,
                CborIndefList<NativeScript> indefList => indefList.Value,
                CborDefListWithTag<NativeScript> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<NativeScript> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<CBootstrapWitness>? BootstrapWitnessSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.BootstrapWitnessSet switch
            {
                CborDefList<CBootstrapWitness> defList => defList.Value,
                CborIndefList<CBootstrapWitness> indefList => indefList.Value,
                CborDefListWithTag<CBootstrapWitness> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<CBootstrapWitness> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.BootstrapWitnessSet switch
            {
                CborDefList<CBootstrapWitness> defList => defList.Value,
                CborIndefList<CBootstrapWitness> indefList => indefList.Value,
                CborDefListWithTag<CBootstrapWitness> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<CBootstrapWitness> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV1ScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusV1ScriptSet switch
            {
                CborDefList<byte[]> defList => defList.Value,
                CborIndefList<byte[]> indefList => indefList.Value,
                CborDefListWithTag<byte[]> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<byte[]> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV1ScriptSet switch
            {
                CborDefList<byte[]> defList => defList.Value,
                CborIndefList<byte[]> indefList => indefList.Value,
                CborDefListWithTag<byte[]> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<byte[]> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<PlutusData>? PlutusDataSet(this TransactionWitnessSet self) =>
        self switch
        {
            AlonzoTransactionWitnessSet alonzoTxWitnessSet => alonzoTxWitnessSet.PlutusDataSet switch
            {
                CborDefList<PlutusData> defList => defList.Value,
                CborIndefList<PlutusData> indefList => indefList.Value,
                CborDefListWithTag<PlutusData> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<PlutusData> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusDataSet switch
            {
                CborDefList<PlutusData> defList => defList.Value,
                CborIndefList<PlutusData> indefList => indefList.Value,
                CborDefListWithTag<PlutusData> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<PlutusData> indefListWithTag => indefListWithTag.Value,
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
                CborDefList<byte[]> defList => defList.Value,
                CborIndefList<byte[]> indefList => indefList.Value,
                CborDefListWithTag<byte[]> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<byte[]> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV3ScriptSet(this TransactionWitnessSet self) =>
        self switch
        {
            PostAlonzoTransactionWitnessSet postAlonzoTxWitnessSet => postAlonzoTxWitnessSet.PlutusV3ScriptSet switch
            {
                CborDefList<byte[]> defList => defList.Value,
                CborIndefList<byte[]> indefList => indefList.Value,
                CborDefListWithTag<byte[]> defListWithTag => defListWithTag.Value,
                CborIndefListWithTag<byte[]> indefListWithTag => indefListWithTag.Value,
                _ => null
            },
            _ => null
        };
}

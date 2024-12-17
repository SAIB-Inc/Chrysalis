

using Chrysalis.Cardano.Core.Types.Block.Transaction.Script;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Extensions;

public static class TransactionWitnessSetExtension
{
    public static IEnumerable<VKeyWitness>? VKeyWitnessSet(this TransactionWitnessSet transactionWitness)
        => transactionWitness switch
        {
            ShelleyTransactionWitnessSet x => x.VKeyWitnessSet switch
            {
                CborDefList<VKeyWitness> list => list.Value,
                CborIndefList<VKeyWitness> list => list.Value,
                CborDefListWithTag<VKeyWitness> list => list.Value,
                CborIndefListWithTag<VKeyWitness> list => list.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionWitnessSet x => x.VKeyWitnessSet switch
            {
                CborDefList<VKeyWitness> list => list.Value,
                CborIndefList<VKeyWitness> list => list.Value,
                CborDefListWithTag<VKeyWitness> list => list.Value,
                CborIndefListWithTag<VKeyWitness> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionWitnessSet x => x.VKeyWitnessSet switch
            {
                CborDefList<VKeyWitness> list => list.Value,
                CborIndefList<VKeyWitness> list => list.Value,
                CborDefListWithTag<VKeyWitness> list => list.Value,
                CborIndefListWithTag<VKeyWitness> list => list.Value,
                _ => throw new NotImplementedException()
            },
            ConwayTransactionWitnessSet x => x.VKeyWitnessSet switch
            {
                CborDefList<VKeyWitness> list => list.Value,
                CborIndefList<VKeyWitness> list => list.Value,
                CborDefListWithTag<VKeyWitness> list => list.Value,
                CborIndefListWithTag<VKeyWitness> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };
    
    public static Redeemers? Redeemers(this TransactionWitnessSet transactionWitness)
        => transactionWitness switch
        {
            AlonzoTransactionWitnessSet x => x.Redeemers,
            BabbageTransactionWitnessSet x => x.Redeemers,
            ConwayTransactionWitnessSet x => x.Redeemers,
            _ => throw new NotImplementedException()
        };
    
    public static IEnumerable<NativeScript>? NativeScriptSet(this TransactionWitnessSet transactionWitness)
        => transactionWitness switch
        {
            ShelleyTransactionWitnessSet x => x.NativeScriptSet switch
            {
                CborDefList<NativeScript> list => list.Value,
                CborIndefList<NativeScript> list => list.Value,
                CborDefListWithTag<NativeScript> list => list.Value,
                CborIndefListWithTag<NativeScript> list => list.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionWitnessSet x => x.NativeScriptSet switch
            {
                CborDefList<NativeScript> list => list.Value,
                CborIndefList<NativeScript> list => list.Value,
                CborDefListWithTag<NativeScript> list => list.Value,
                CborIndefListWithTag<NativeScript> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionWitnessSet x => x.NativeScriptSet switch
            {
                CborDefList<NativeScript> list => list.Value,
                CborIndefList<NativeScript> list => list.Value,
                CborDefListWithTag<NativeScript> list => list.Value,
                CborIndefListWithTag<NativeScript> list => list.Value,
                _ => throw new NotImplementedException()
            },
            ConwayTransactionWitnessSet x => x.NativeScriptSet switch
            {
                CborDefList<NativeScript> list => list.Value,
                CborIndefList<NativeScript> list => list.Value,
                CborDefListWithTag<NativeScript> list => list.Value,
                CborIndefListWithTag<NativeScript> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };
    
    public static IEnumerable<BootstrapWitness>? BootstrapWitnessSet(this TransactionWitnessSet transactionWitness)
        => transactionWitness switch
        {
            ShelleyTransactionWitnessSet x => x.BootstrapWitnessSet switch
            {
                CborDefList<BootstrapWitness> list => list.Value,
                CborIndefList<BootstrapWitness> list => list.Value,
                CborDefListWithTag<BootstrapWitness> list => list.Value,
                CborIndefListWithTag<BootstrapWitness> list => list.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionWitnessSet x => x.BootstrapWitnessSet switch
            {
                CborDefList<BootstrapWitness> list => list.Value,
                CborIndefList<BootstrapWitness> list => list.Value,
                CborDefListWithTag<BootstrapWitness> list => list.Value,
                CborIndefListWithTag<BootstrapWitness> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionWitnessSet x => x.BootstrapWitnessSet switch
            {
                CborDefList<BootstrapWitness> list => list.Value,
                CborIndefList<BootstrapWitness> list => list.Value,
                CborDefListWithTag<BootstrapWitness> list => list.Value,
                CborIndefListWithTag<BootstrapWitness> list => list.Value,
                _ => throw new NotImplementedException()
            },
            ConwayTransactionWitnessSet x => x.BootstrapWitnessSet switch
            {
                CborDefList<BootstrapWitness> list => list.Value,
                CborIndefList<BootstrapWitness> list => list.Value,
                CborDefListWithTag<BootstrapWitness> list => list.Value,
                CborIndefListWithTag<BootstrapWitness> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<PlutusData>? PlutusDataSet(this TransactionWitnessSet transactionWitness)
        => transactionWitness switch
        {
            AlonzoTransactionWitnessSet x => x.PlutusDataSet switch
            {
                CborDefList<PlutusData> list => list.Value,
                CborIndefList<PlutusData> list => list.Value,
                CborDefListWithTag<PlutusData> list => list.Value,
                CborIndefListWithTag<PlutusData> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionWitnessSet x => x.PlutusDataSet switch
            {
                CborDefList<PlutusData> list => list.Value,
                CborIndefList<PlutusData> list => list.Value,
                CborDefListWithTag<PlutusData> list => list.Value,
                CborIndefListWithTag<PlutusData> list => list.Value,
                _ => throw new NotImplementedException()
            },
            ConwayTransactionWitnessSet x => x.PlutusDataSet switch
            {
                CborDefList<PlutusData> list => list.Value,
                CborIndefList<PlutusData> list => list.Value,
                CborDefListWithTag<PlutusData> list => list.Value,
                CborIndefListWithTag<PlutusData> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };
    
    public static IEnumerable<byte[]>? PlutusV1ScriptSet(this TransactionWitnessSet transactionWitness)
        => transactionWitness switch
        {
            AlonzoTransactionWitnessSet x => x.PlutusV1ScriptSet switch
            {
                CborDefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborDefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                _ => throw new NotImplementedException()
            },
            BabbageTransactionWitnessSet x => x.PlutusV1ScriptSet switch
            {
                CborDefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborDefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                _ => throw new NotImplementedException()
            },
            ConwayTransactionWitnessSet x => x.PlutusV1ScriptSet switch
            {
                CborDefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborDefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<byte[]>? PlutusV2ScriptSet(this TransactionWitnessSet transactionWitness)
        => transactionWitness switch
        {
            BabbageTransactionWitnessSet x => x.PlutusV2ScriptSet switch
            {
                CborDefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborDefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                _ => throw new NotImplementedException()
            },
            ConwayTransactionWitnessSet x => x.PlutusV2ScriptSet switch
            {
                CborDefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborDefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };
    
    public static IEnumerable<byte[]>? PlutusV3ScriptSet(this TransactionWitnessSet transactionWitness)
        => transactionWitness switch
        {
            ConwayTransactionWitnessSet x => x.PlutusV3ScriptSet switch
            {
                CborDefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefList<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborDefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                CborIndefListWithTag<CborBytes> list => list.Value.Select(x => x.Value).ToList(),
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };
}
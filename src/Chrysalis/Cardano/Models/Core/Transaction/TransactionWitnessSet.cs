using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Script;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Map)]
public record TransactionWitnessSet(
    [CborProperty(0)] CborIndefiniteList<VKeyWitness>? VKeyWitnessSet,
    [CborProperty(1)] CborIndefiniteList<NativeScript>? NativeScriptSet, // @TODO: Modify T Parameter
    [CborProperty(2)] CborIndefiniteList<BootstrapWitness>? BootstrapWitnessSet,
    [CborProperty(3)] CborIndefiniteList<CborBytes>? PlutusV1ScriptSet,   
    [CborProperty(4)] CborIndefiniteList<PlutusData>? PlutusDataSet, // @TODO: Modify T Parameter   
    [CborProperty(5)] Redeemers? Redeemers,
    [CborProperty(6)] CborIndefiniteList<CborBytes>? PlutusV2ScriptSet,
    [CborProperty(7)] CborIndefiniteList<CborBytes>? PlutusV3ScriptSet
    
) : ICbor;

using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Script;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Map)]
public record TransactionWitnessSet(
    [CborProperty(0)] CborIndefiniteList<VKeyWitness>? VKeyWitnessSet,
    [CborProperty(1)] CborIndefiniteList<NativeScript>? NativeScriptSet, // @TODO: Modify T Parameter
    [CborProperty(2)] CborIndefiniteList<BootstrapWitness>? BootstrapWitnessSet,
    [CborProperty(3)] CborIndefiniteList<PlutusV1Script>? PlutusV1ScriptSet,   
    [CborProperty(4)] CborIndefiniteList<BootstrapWitness>? PlutusDataSet, // @TODO: Modify T Parameter   
    [CborProperty(5)] Redeemers? Redeemers,
    [CborProperty(6)] CborIndefiniteList<PlutusV2Script>? PlutusV2ScriptSet,
    [CborProperty(7)] CborIndefiniteList<PlutusV3Script>? PlutusV3ScriptSet
    
) : ICbor;

using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Map)]
public record TransactionWitnessSet(
    [CborProperty(0)] CborIndefiniteList<VKeyWitness>? VKeyWitnessSet,
    [CborProperty(1)] CborIndefiniteList<BootstrapWitness>? NativeScriptSet, // @TODO: Modify T Parameter
    [CborProperty(2)] CborIndefiniteList<BootstrapWitness>? BootstrapWitnessSet,
    [CborProperty(3)] CborIndefiniteList<BootstrapWitness>? PlutusV1ScriptSet, // @TODO: Modify T Parameter    
    [CborProperty(4)] CborIndefiniteList<BootstrapWitness>? PlutusDataSet, // @TODO: Modify T Parameter   
    [CborProperty(5)] Redeemers? Redeemers,
    [CborProperty(6)] CborIndefiniteList<BootstrapWitness>? PlutusV2ScriptSet, // @TODO: Modify T Parameter
    [CborProperty(7)] CborIndefiniteList<BootstrapWitness>? PlutusV3ScriptSet // @TODO: Modify T Parameter
) : ICbor;

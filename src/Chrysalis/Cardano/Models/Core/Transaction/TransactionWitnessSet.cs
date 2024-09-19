using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Script;
using Chrysalis.Cardano.Models.Plutus;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Map)]
public record TransactionWitnessSet(
    [CborProperty(0)] CborDefiniteList<VKeyWitness>? VKeyWitnessSet,
    [CborProperty(1)] CborDefiniteList<NativeScript>? NativeScriptSet,
    [CborProperty(2)] CborDefiniteList<BootstrapWitness>? BootstrapWitnessSet,
    [CborProperty(3)] CborDefiniteList<CborBytes>? PlutusV1ScriptSet,   
    [CborProperty(4)] CborDefiniteList<PlutusData>? PlutusDataSet,
    [CborProperty(5)] Redeemers? Redeemers,
    [CborProperty(6)] CborDefiniteList<CborBytes>? PlutusV2ScriptSet,
    [CborProperty(7)] CborDefiniteList<CborBytes>? PlutusV3ScriptSet
) : ICbor;

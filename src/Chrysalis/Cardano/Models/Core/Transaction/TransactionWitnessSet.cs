using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Script;
using Chrysalis.Cardano.Models.Plutus;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Map)]
public record TransactionWitnessSet(
    [CborProperty(0)] Option<CborDefiniteList<VKeyWitness>> VKeyWitnessSet,
    [CborProperty(1)] Option<CborDefiniteList<NativeScript>> NativeScriptSet,
    [CborProperty(2)] Option<CborDefiniteList<BootstrapWitness>> BootstrapWitnessSet,
    [CborProperty(3)] Option<CborDefiniteList<CborBytes>> PlutusV1ScriptSet,   
    [CborProperty(4)] Option<CborIndefiniteList<PlutusData>> PlutusDataSet,
    [CborProperty(5)] Option<Redeemers> Redeemers,
    [CborProperty(6)] Option<CborDefiniteList<CborBytes>> PlutusV2ScriptSet,
    [CborProperty(7)] Option<CborDefiniteList<CborBytes>> PlutusV3ScriptSet
) : ICbor;

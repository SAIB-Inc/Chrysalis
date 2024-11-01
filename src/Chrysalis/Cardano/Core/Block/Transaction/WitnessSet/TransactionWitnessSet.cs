using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Map)]
public record TransactionWitnessSet(
    [CborProperty(0)] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet,
    [CborProperty(1)] CborMaybeIndefList<NativeScript>? NativeScriptSet,
    [CborProperty(2)] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet,
    [CborProperty(3)] CborMaybeIndefList<CborBytes>? PlutusV1ScriptSet,   
    [CborProperty(4)] CborMaybeIndefList<PlutusData>? PlutusDataSet,
    [CborProperty(5)] Redeemers? Redeemers,
    [CborProperty(6)] CborMaybeIndefList<CborBytes>? PlutusV2ScriptSet,
    [CborProperty(7)] CborMaybeIndefList<CborBytes>? PlutusV3ScriptSet
) : RawCbor;

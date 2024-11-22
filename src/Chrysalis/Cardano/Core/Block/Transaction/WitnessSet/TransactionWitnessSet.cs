using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Map)]
public record TransactionWitnessSet : RawCbor
{
    [CborProperty(0)] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet { get; set; }
    [CborProperty(1)] CborMaybeIndefList<NativeScript>? NativeScriptSet { get; set; }
    [CborProperty(2)] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet { get; set; }
    [CborProperty(3)] CborMaybeIndefList<CborBytes>? PlutusV1ScriptSet { get; set; }
    [CborProperty(4)] CborMaybeIndefList<PlutusData>? PlutusDataSet { get; set; }
    [CborProperty(5)] Redeemers? Redeemers { get; set; }
    [CborProperty(6)] CborMaybeIndefList<CborBytes>? PlutusV2ScriptSet { get; set; }
    [CborProperty(7)] CborMaybeIndefList<CborBytes>? PlutusV3ScriptSet { get; set; }

    public TransactionWitnessSet() { }

    public TransactionWitnessSet(
        CborMaybeIndefList<VKeyWitness>? vKeyWitnessSet,
        CborMaybeIndefList<NativeScript>? nativeScriptSet,
        CborMaybeIndefList<BootstrapWitness>? bootstrapWitnessSet,
        CborMaybeIndefList<CborBytes>? plutusV1ScriptSet,
        CborMaybeIndefList<PlutusData>? plutusDataSet,
        Redeemers? redeemers,
        CborMaybeIndefList<CborBytes>? plutusV2ScriptSet,
        CborMaybeIndefList<CborBytes>? plutusV3ScriptSet
    )
    {
        VKeyWitnessSet = vKeyWitnessSet;
        NativeScriptSet = nativeScriptSet;
        BootstrapWitnessSet = bootstrapWitnessSet;
        PlutusV1ScriptSet = plutusV1ScriptSet;
        PlutusDataSet = plutusDataSet;
        Redeemers = redeemers;
        PlutusV2ScriptSet = plutusV2ScriptSet;
        PlutusV3ScriptSet = plutusV3ScriptSet;
    }
}

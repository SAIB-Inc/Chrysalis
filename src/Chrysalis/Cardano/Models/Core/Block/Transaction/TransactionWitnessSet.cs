using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Block.Transaction.Script;
using Chrysalis.Cardano.Models.Core.Block.Transaction.WitnessSet;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction;

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

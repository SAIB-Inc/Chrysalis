using Chrysalis.Cbor.Attributes;

using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

// [CborSerializable]
[CborUnion]
public abstract partial record TransactionWitnessSet : CborBase<TransactionWitnessSet>
{
    // [CborSerializable]
    [CborMap]
    public partial record AlonzoTransactionWitnessSet(
        [CborIndex(0)] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet,
        [CborIndex(1)] CborMaybeIndefList<NativeScript>? NativeScriptSet,
        [CborIndex(2)] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet,
        [CborIndex(3)] CborMaybeIndefList<byte[]>? PlutusV1ScriptSet,
        [CborIndex(4)] CborMaybeIndefList<PlutusData>? PlutusDataSet,
        [CborIndex(5)] Redeemers? Redeemers
    ) : TransactionWitnessSet;

    // [CborSerializable]
    [CborMap]
    public partial record PostAlonzoTransactionWitnessSet(
        [CborIndex(0)] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet,
        [CborIndex(1)] CborMaybeIndefList<NativeScript>? NativeScriptSet,
        [CborIndex(2)] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet,
        [CborIndex(3)] CborMaybeIndefList<byte[]>? PlutusV1ScriptSet,
        [CborIndex(4)] CborMaybeIndefList<PlutusData>? PlutusDataSet,
        [CborIndex(5)] Redeemers? Redeemers,
        [CborIndex(6)] CborMaybeIndefList<byte[]>? PlutusV2ScriptSet,
        [CborIndex(7)] CborMaybeIndefList<byte[]>? PlutusV3ScriptSet
    ) : TransactionWitnessSet;
}

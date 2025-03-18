using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborSerializable]
[CborUnion]
public abstract partial record TransactionWitnessSet : CborBase<TransactionWitnessSet>
{
    [CborSerializable]
    [CborMap]
    public partial record AlonzoTransactionWitnessSet(
        [CborOrder(0)] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet,
        [CborOrder(1)] CborMaybeIndefList<NativeScript>? NativeScriptSet,
        [CborOrder(2)] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet,
        [CborOrder(3)] CborMaybeIndefList<byte[]>? PlutusV1ScriptSet,
        [CborOrder(4)] CborMaybeIndefList<PlutusData>? PlutusDataSet,
        [CborOrder(5)] Redeemers? Redeemers
    ) : TransactionWitnessSet;

    [CborSerializable]
    [CborMap]
    public partial record PostAlonzoTransactionWitnessSet(
        [CborProperty("0")] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet,
        [CborProperty("1")] CborMaybeIndefList<NativeScript>? NativeScriptSet,
        [CborProperty("2")] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet,
        [CborProperty("3")] CborMaybeIndefList<byte[]>? PlutusV1ScriptSet,
        [CborProperty("4")] CborMaybeIndefList<PlutusData>? PlutusDataSet,
        [CborProperty("5")] Redeemers? Redeemers,
        [CborProperty("6")] CborMaybeIndefList<byte[]>? PlutusV2ScriptSet,
        [CborProperty("7")] CborMaybeIndefList<byte[]>? PlutusV3ScriptSet
    ) : TransactionWitnessSet;
}

using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
[CborSerializable]
[CborUnion]
public abstract partial record TransactionWitnessSet : CborBase
{
}

[CborSerializable]
[CborMap]
public partial record AlonzoTransactionWitnessSet(
    [CborProperty(0)] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet,
    [CborProperty(1)] CborMaybeIndefList<NativeScript>? NativeScriptSet,
    [CborProperty(2)] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet,
    [CborProperty(3)] CborMaybeIndefList<byte[]>? PlutusV1ScriptSet,
    [CborProperty(4)] CborMaybeIndefList<PlutusData>? PlutusDataSet,
    [CborProperty(5)] Redeemers? Redeemers
) : TransactionWitnessSet, ICborPreserveRaw;

[CborSerializable]
[CborMap]
public partial record PostAlonzoTransactionWitnessSet(
    [CborProperty(0)] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet,
    [CborProperty(1)] CborMaybeIndefList<NativeScript>? NativeScriptSet,
    [CborProperty(2)] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet,
    [CborProperty(3)] CborMaybeIndefList<byte[]>? PlutusV1ScriptSet,
    [CborProperty(4)] CborMaybeIndefList<PlutusData>? PlutusDataSet,
    [CborProperty(5)] Redeemers? Redeemers,
    [CborProperty(6)] CborMaybeIndefList<byte[]>? PlutusV2ScriptSet,
    [CborProperty(7)] CborMaybeIndefList<byte[]>? PlutusV3ScriptSet
) : TransactionWitnessSet, ICborPreserveRaw;
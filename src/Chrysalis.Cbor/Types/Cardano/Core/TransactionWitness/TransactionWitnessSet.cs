using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

/// <summary>
/// Abstract base for transaction witness sets across different Cardano eras.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record TransactionWitnessSet : CborBase
{
}

/// <summary>
/// An Alonzo-era transaction witness set with support for VKey witnesses, native scripts, bootstrap witnesses, Plutus V1 scripts, Plutus data, and redeemers.
/// </summary>
/// <param name="VKeyWitnessSet">The optional verification key witnesses.</param>
/// <param name="NativeScriptSet">The optional native scripts.</param>
/// <param name="BootstrapWitnessSet">The optional bootstrap (Byron-era) witnesses.</param>
/// <param name="PlutusV1ScriptSet">The optional Plutus V1 script bytes.</param>
/// <param name="PlutusDataSet">The optional Plutus data values.</param>
/// <param name="Redeemers">The optional redeemers.</param>
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

/// <summary>
/// A post-Alonzo transaction witness set with additional support for Plutus V2 and V3 scripts.
/// </summary>
/// <param name="VKeyWitnessSet">The optional verification key witnesses.</param>
/// <param name="NativeScriptSet">The optional native scripts.</param>
/// <param name="BootstrapWitnessSet">The optional bootstrap (Byron-era) witnesses.</param>
/// <param name="PlutusV1ScriptSet">The optional Plutus V1 script bytes.</param>
/// <param name="PlutusDataSet">The optional Plutus data values.</param>
/// <param name="Redeemers">The optional redeemers.</param>
/// <param name="PlutusV2ScriptSet">The optional Plutus V2 script bytes.</param>
/// <param name="PlutusV3ScriptSet">The optional Plutus V3 script bytes.</param>
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

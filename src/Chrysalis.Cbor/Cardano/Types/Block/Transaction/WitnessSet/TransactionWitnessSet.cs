using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborConverter(typeof(UnionConverter))]
public abstract record TransactionWitnessSet : CborBase;

[CborConverter(typeof(CustomMapConverter))]
public record AlonzoTransactionWitnessSet(
    [CborIndex(0)] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet,
    [CborIndex(1)] CborMaybeIndefList<NativeScript>? NativeScriptSet,
    [CborIndex(2)] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet,
    [CborIndex(3)] CborMaybeIndefList<CborBytes>? PlutusV1ScriptSet,
    [CborIndex(4)] CborMaybeIndefList<PlutusData>? PlutusDataSet,
    [CborIndex(5)] Redeemers? Redeemers
) : TransactionWitnessSet;

[CborConverter(typeof(CustomMapConverter))]
[CborOptions(IsDefinite = true)]
public record PostAlonzoTransactionWitnessSet(
    [CborIndex(0)] CborMaybeIndefList<VKeyWitness>? VKeyWitnessSet,
    [CborIndex(1)] CborMaybeIndefList<NativeScript>? NativeScriptSet,
    [CborIndex(2)] CborMaybeIndefList<BootstrapWitness>? BootstrapWitnessSet,
    [CborIndex(3)] CborMaybeIndefList<CborBytes>? PlutusV1ScriptSet,
    [CborIndex(4)] CborMaybeIndefList<PlutusData>? PlutusDataSet,
    [CborIndex(5)] Redeemers? Redeemers,
    [CborIndex(6)] CborMaybeIndefList<CborBytes>? PlutusV2ScriptSet,
    [CborIndex(7)] CborMaybeIndefList<CborBytes>? PlutusV3ScriptSet
) : TransactionWitnessSet;
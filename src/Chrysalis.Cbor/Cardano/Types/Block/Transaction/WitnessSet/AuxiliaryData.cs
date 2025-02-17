using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborConverter(typeof(UnionConverter))]
public abstract record AuxiliaryData : CborBase;


[CborConverter(typeof(CustomMapConverter))]
[CborOptions(Tag = 259)]
public record PostAlonzoAuxiliaryDataMap(
    [CborIndex(0)] Metadata? Metadata,
    [CborIndex(1)] CborDefList<NativeScript>? NativeScriptSet,
    [CborIndex(2)] CborDefList<CborBytes>? PlutusV1ScriptSet,
    [CborIndex(3)] CborDefList<CborBytes>? PlutusV2ScriptSet,
    [CborIndex(4)] CborDefList<CborBytes>? PlutusV3ScriptSet
) : AuxiliaryData;


[CborConverter(typeof(MapConverter))]
public record Metadata(Dictionary<CborUlong, TransactionMetadatum> Value) : AuxiliaryData;


[CborConverter(typeof(CustomListConverter))]
public record ShellyMaAuxiliaryData(
    [CborIndex(0)] Metadata TransactionMetadata,
    [CborIndex(1)] CborDefList<NativeScript> AuxiliaryScripts
) : AuxiliaryData;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;

[CborConverter(typeof(UnionConverter))]
public abstract record AuxiliaryData : CborBase;


[CborConverter(typeof(CustomMapConverter))]
[CborTag(259)]
public record PostAlonzoAuxiliaryDataMap(
    [CborProperty(0)] Metadata? Metadata,
    [CborProperty(1)] CborDefList<NativeScript>? NativeScriptSet,
    [CborProperty(2)] CborDefList<CborBytes>? PlutusV1ScriptSet,
    [CborProperty(3)] CborDefList<CborBytes>? PlutusV2ScriptSet,
    [CborProperty(4)] CborDefList<CborBytes>? PlutusV3ScriptSet
) : AuxiliaryData;


[CborConverter(typeof(MapConverter))]
public record Metadata(Dictionary<CborUlong, TransactionMetadatum> Value) : AuxiliaryData;


[CborConverter(typeof(CustomListConverter))]
public record ShellyMaAuxiliaryData(
    [CborProperty(0)] Metadata TransactionMetadata,
    [CborProperty(1)] CborDefList<NativeScript> AuxiliaryScripts
) : AuxiliaryData;
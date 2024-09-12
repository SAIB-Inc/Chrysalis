using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(Metadata),
    typeof(ShellyMaAuxiliaryData),
    typeof(PostAlonzoAuxiliaryData),
])]
public record AuxiliaryData : ICbor;
public record Metadata(
    Dictionary<CborUlong, TransactionMetadatum> MetadataMap
): AuxiliaryData;

[CborSerializable(CborType.List)]
public record ShellyMaAuxiliaryData(
    [CborProperty(0)] Metadata TransactionMetadata,
    [CborProperty(1)] CborIndefiniteList<BootstrapWitness> AuxiliaryScripts
): AuxiliaryData;

[CborSerializable(CborType.Map)]
public record PostAlonzoAuxiliaryData(
    [CborProperty(0)] Metadata Metadata,
    [CborProperty(1)] CborIndefiniteList<BootstrapWitness>? NativeScriptSet,
    [CborProperty(2)] CborIndefiniteList<BootstrapWitness>? PlutusV1ScriptSet,
    [CborProperty(3)] CborIndefiniteList<BootstrapWitness>? PlutusV2ScriptSet,
    [CborProperty(4)] CborIndefiniteList<BootstrapWitness>? PlutusV3ScriptSet
): AuxiliaryData;
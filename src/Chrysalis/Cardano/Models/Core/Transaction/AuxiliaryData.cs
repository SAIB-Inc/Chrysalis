using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Script;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(Metadata),
    typeof(ShellyMaAuxiliaryData),
    typeof(PostAlonzoAuxiliaryData),
])]
public record AuxiliaryData : ICbor;

public record Metadata(
    CborMap<CborUlong, TransactionMetadatum> MetadataMap
): AuxiliaryData;

[CborSerializable(CborType.List)]
public record ShellyMaAuxiliaryData(
    [CborProperty(0)] Metadata TransactionMetadata,
    [CborProperty(1)] CborDefiniteList<NativeScript> AuxiliaryScripts
): AuxiliaryData;

[CborSerializable(CborType.Map)]
public record PostAlonzoAuxiliaryData(
    [CborProperty(0)] Metadata? Metadata,
    [CborProperty(1)] CborDefiniteList<NativeScript>? NativeScriptSet,
    [CborProperty(2)] CborDefiniteList<CborBytes>? PlutusV1ScriptSet,
    [CborProperty(3)] CborDefiniteList<CborBytes>? PlutusV2ScriptSet,
    [CborProperty(4)] CborDefiniteList<CborBytes>? PlutusV3ScriptSet
): AuxiliaryData;
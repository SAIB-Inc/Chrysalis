using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Script;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(PostAlonzoAuxiliaryData),
    typeof(Metadata),
    typeof(ShellyMaAuxiliaryData),
])]
public interface AuxiliaryData : ICbor;

[CborSerializable(CborType.Tag, Index = 259)]
public record PostAlonzoAuxiliaryData(PostAlonzoAuxiliaryDataMap Value): AuxiliaryData;

[CborSerializable(CborType.Map)]
public record PostAlonzoAuxiliaryDataMap(
    [CborProperty(0)] Metadata? Metadata,
    [CborProperty(1)] CborDefiniteList<NativeScript>? NativeScriptSet,
    [CborProperty(2)] CborDefiniteList<CborBytes>? PlutusV1ScriptSet,
    [CborProperty(3)] CborDefiniteList<CborBytes>? PlutusV2ScriptSet,
    [CborProperty(4)] CborDefiniteList<CborBytes>? PlutusV3ScriptSet
): ICbor;

public record Metadata(Dictionary<CborUlong, TransactionMetadatum> Value)
    : CborMap<CborUlong, TransactionMetadatum>(Value), AuxiliaryData;

[CborSerializable(CborType.List)]
public record ShellyMaAuxiliaryData(
    [CborProperty(0)] Metadata TransactionMetadata,
    [CborProperty(1)] CborDefiniteList<NativeScript> AuxiliaryScripts
): AuxiliaryData;
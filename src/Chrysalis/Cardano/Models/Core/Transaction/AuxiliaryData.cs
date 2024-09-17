using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Script;
using Chrysalis.Cardano.Models.Plutus;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(Metadata),
    typeof(ShellyMaAuxiliaryData),
    typeof(PostAlonzoAuxiliaryData),
])]
public interface AuxiliaryData : ICbor;

public record Metadata(Dictionary<CborUlong, TransactionMetadatum> Value)
    : CborMap<CborUlong, TransactionMetadatum>(Value), AuxiliaryData;

[CborSerializable(CborType.List)]
public record ShellyMaAuxiliaryData(
    [CborProperty(0)] Metadata TransactionMetadata,
    [CborProperty(1)] CborDefiniteList<NativeScript> AuxiliaryScripts
): AuxiliaryData;

[CborSerializable(CborType.Map)]
public record PostAlonzoAuxiliaryData(
    [CborProperty(0)] Option<Metadata> Metadata,
    [CborProperty(1)] Option<CborDefiniteList<NativeScript>> NativeScriptSet,
    [CborProperty(2)] Option<CborDefiniteList<CborBytes>> PlutusV1ScriptSet,
    [CborProperty(3)] Option<CborDefiniteList<CborBytes>> PlutusV2ScriptSet,
    [CborProperty(4)] Option<CborDefiniteList<CborBytes>> PlutusV3ScriptSet
): AuxiliaryData;
using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(PostAlonzoAuxiliaryData),
    typeof(Metadata),
    typeof(ShellyMaAuxiliaryData),
])]
public interface AuxiliaryData : ICbor;

[CborSerializable(CborType.Tag, Index = 259)]
public record PostAlonzoAuxiliaryData(PostAlonzoAuxiliaryDataMap Value) : AuxiliaryData
{
    public byte[]? Raw { get; set; }
}

[CborSerializable(CborType.Map)]
public record PostAlonzoAuxiliaryDataMap : RawCbor
{
    [CborProperty(0)] Metadata? Metadata { get; set; }
    [CborProperty(1)] CborDefiniteList<NativeScript>? NativeScriptSet { get; set; }
    [CborProperty(2)] CborDefiniteList<CborBytes>? PlutusV1ScriptSet { get; set; }
    [CborProperty(3)] CborDefiniteList<CborBytes>? PlutusV2ScriptSet { get; set; }
    [CborProperty(4)] CborDefiniteList<CborBytes>? PlutusV3ScriptSet { get; set; }

    public PostAlonzoAuxiliaryDataMap() { }

    public PostAlonzoAuxiliaryDataMap(Metadata? metadata, CborDefiniteList<NativeScript>? nativeScriptSet, CborDefiniteList<CborBytes>? plutusV1ScriptSet, CborDefiniteList<CborBytes>? plutusV2ScriptSet, CborDefiniteList<CborBytes>? plutusV3ScriptSet)
    {
        Metadata = metadata;
        NativeScriptSet = nativeScriptSet;
        PlutusV1ScriptSet = plutusV1ScriptSet;
        PlutusV2ScriptSet = plutusV2ScriptSet;
        PlutusV3ScriptSet = plutusV3ScriptSet;
    }
}

public record Metadata(Dictionary<CborUlong, TransactionMetadatum> Value)
    : CborMap<CborUlong, TransactionMetadatum>(Value), AuxiliaryData;

[CborSerializable(CborType.List)]
public record ShellyMaAuxiliaryData(
    [CborProperty(0)] Metadata TransactionMetadata,
    [CborProperty(1)] CborDefiniteList<NativeScript> AuxiliaryScripts
) : AuxiliaryData
{
    public byte[]? Raw { get; set; }
}

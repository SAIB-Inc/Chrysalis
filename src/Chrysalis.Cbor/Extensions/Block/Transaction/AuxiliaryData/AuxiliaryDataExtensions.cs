using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.AuxiliaryData;
using CAuxiliaryData = Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.AuxiliaryData;
using CMetadata = Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.AuxiliaryData.Metadata;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.AuxiliaryData;

public static class AuxiliaryDataExtensions
{
    public static CMetadata? Metadata(this CAuxiliaryData self) =>
        self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.MetadataValue,
            CMetadata metadata => metadata,
            ShellyMaAuxiliaryData shellyMaAuxiliaryData => shellyMaAuxiliaryData.TransactionMetadata,
            _ => null
        };

    public static IEnumerable<NativeScript>? NativeScriptSet(this CAuxiliaryData self) =>
        self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.NativeScriptSet?.Value,
            ShellyMaAuxiliaryData shellyMaAuxiliaryData => shellyMaAuxiliaryData.AuxiliaryScripts.Value,
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV1ScriptSet(this CAuxiliaryData self) =>
        self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.PlutusV1ScriptSet?.Value,
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV2ScriptSet(this CAuxiliaryData self) =>
        self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.PlutusV2ScriptSet?.Value,
            _ => null
        };
    
    public static IEnumerable<byte[]>? PlutusV3ScriptSet(this CAuxiliaryData self) =>
        self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.PlutusV3ScriptSet?.Value,
            _ => null
        };
}
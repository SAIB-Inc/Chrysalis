using CMetadata = Chrysalis.Cbor.Types.Cardano.Core.Metadata;
using CAuxiliaryData = Chrysalis.Cbor.Types.Cardano.Core.AuxiliaryData;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core;

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
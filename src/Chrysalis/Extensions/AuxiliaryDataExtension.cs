using Chrysalis.Cardano.Core;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Extensions;

public static class AuxiliaryDataExtension
{
    public static Dictionary<CborUlong, TransactionMetadatum>? Metadata(this AuxiliaryData auxiliaryData) 
        => auxiliaryData switch
        {
            Metadata metadata => metadata.Value,
            PostAlonzoAuxiliaryData post => post.Value.Metadata?.Value,
            ShellyMaAuxiliaryData shelley => shelley.TransactionMetadata.Value,
            _ => null
        };

    public static IEnumerable<NativeScript>? NativeScripts(this AuxiliaryData auxiliaryData) 
        => auxiliaryData switch
        {
            PostAlonzoAuxiliaryData post => post.Value.NativeScriptSet?.Value,
            ShellyMaAuxiliaryData shelley => shelley.AuxiliaryScripts.Value,
            _ => null
        };
    
    public static IEnumerable<CborBytes>? PlutusV1Scripts(this AuxiliaryData auxiliaryData) 
        => auxiliaryData switch
        {
            PostAlonzoAuxiliaryData post => post.Value.PlutusV1ScriptSet?.Value,
            _ => null
        };
    
    public static IEnumerable<CborBytes>? PlutusV2Scripts(this AuxiliaryData auxiliaryData) 
        => auxiliaryData switch
    {
        PostAlonzoAuxiliaryData post => post.Value.PlutusV2ScriptSet?.Value,
        _ => null
    };
    public static IEnumerable<CborBytes>? PlutusV3Scripts(this AuxiliaryData auxiliaryData) => auxiliaryData switch
    {
        PostAlonzoAuxiliaryData post => post.Value.PlutusV3ScriptSet?.Value,
        _ => null
    };
}
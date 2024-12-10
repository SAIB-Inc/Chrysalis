using Chrysalis.Cardano.Core;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Extensions;

public static class AuxiliaryDataExtension
{
    public static Dictionary<ulong, TransactionMetadatum>? Metadata(this AuxiliaryData auxiliaryData) 
        => auxiliaryData switch
        {
            Metadata metadata => metadata.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            PostAlonzoAuxiliaryData post => post.Value.Metadata?.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            ShellyMaAuxiliaryData shelley => shelley.TransactionMetadata.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            _ => null
        };

    public static IEnumerable<NativeScript>? NativeScripts(this AuxiliaryData auxiliaryData) 
        => auxiliaryData switch
        {
            PostAlonzoAuxiliaryData post => post.Value.NativeScriptSet?.Value,
            ShellyMaAuxiliaryData shelley => shelley.AuxiliaryScripts.Value,
            _ => null
        };
    
    public static IEnumerable<byte[]>? PlutusV1Scripts(this AuxiliaryData auxiliaryData) 
        => auxiliaryData switch
        {
            PostAlonzoAuxiliaryData post => post.Value.PlutusV1ScriptSet?.Value.Select(script => script.Value),
            _ => null
        };
    
    public static IEnumerable<byte[]>? PlutusV2Scripts(this AuxiliaryData auxiliaryData) 
        => auxiliaryData switch
    {
        PostAlonzoAuxiliaryData post => post.Value.PlutusV2ScriptSet?.Value.Select(script => script.Value),
            _ => null
        };
    
    public static IEnumerable<byte[]>? PlutusV3Scripts(this AuxiliaryData auxiliaryData) 
        => auxiliaryData switch
        {
            PostAlonzoAuxiliaryData post => post.Value.PlutusV3ScriptSet?.Value.Select(script => script.Value),
            _ => null
        };
}
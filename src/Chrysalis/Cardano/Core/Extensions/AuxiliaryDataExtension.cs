using Chrysalis.Cardano.Core.Types.Block.Transaction;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Script;

namespace Chrysalis.Cardano.Core.Extensions;

public static class AuxiliaryDataExtension
{
    public static Dictionary<ulong, TransactionMetadatum>? Metadata(this AuxiliaryData auxiliaryData)
        => auxiliaryData switch
        {
            Metadata metadata => metadata.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            PostAlonzoAuxiliaryDataMap post => post.Metadata?.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            ShellyMaAuxiliaryData shelley => shelley.TransactionMetadata.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            _ => null
        };

    public static IEnumerable<NativeScript>? NativeScripts(this AuxiliaryData auxiliaryData)
        => auxiliaryData switch
        {
            PostAlonzoAuxiliaryDataMap post => post.NativeScriptSet?.Value,
            ShellyMaAuxiliaryData shelley => shelley.AuxiliaryScripts.Value,
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV1Scripts(this AuxiliaryData auxiliaryData)
        => auxiliaryData switch
        {
            PostAlonzoAuxiliaryDataMap post => post.PlutusV1ScriptSet?.Value.Select(script => script.Value),
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV2Scripts(this AuxiliaryData auxiliaryData)
        => auxiliaryData switch
        {
            PostAlonzoAuxiliaryDataMap post => post.PlutusV2ScriptSet?.Value.Select(script => script.Value),
            _ => null
        };

    public static IEnumerable<byte[]>? PlutusV3Scripts(this AuxiliaryData auxiliaryData)
        => auxiliaryData switch
        {
            PostAlonzoAuxiliaryDataMap post => post.PlutusV3ScriptSet?.Value.Select(script => script.Value),
            _ => null
        };
}
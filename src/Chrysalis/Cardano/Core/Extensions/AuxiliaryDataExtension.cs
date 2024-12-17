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

    public static Dictionary<ulong, TransactionMetadatum> GetMetadata(this AuxiliaryData data)
        => data switch
        {
            PostAlonzoAuxiliaryDataMap x => x.Metadata?.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value) ?? throw new InvalidOperationException("Metadata cannot be null in PostAlonzoAuxiliaryData."),
            Metadata x => x.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            ShellyMaAuxiliaryData x => x.TransactionMetadata.Value.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value),
            _ => throw new NotImplementedException()
        };

    public static object GetMetadataValue(this TransactionMetadatum data)
    => data switch
    {
        MetadatumMap x => x.Value,
        MetadatumList x => x.Value,
        MetadatumInt x => x switch
        {
            MetadatumIntLong longValue => longValue.Value,
            MetadatumIntUlong ulongValue => ulongValue.Value,
            _ => throw new NotImplementedException("Unhandled MetadatumInt type.")
        },
        MetadatumBytes x => x.Value,
        MetadataText x => x.Value,
        _ => throw new NotImplementedException("Unsupported TransactionMetadatum type.")
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

    public static TransactionMetadatum? GetMetadatum(this AuxiliaryData auxiliaryData, ulong label)
        => auxiliaryData.Metadata()?.TryGetValue(label, out var datum) == true ? datum : null;

    public static bool HasMetadata(this AuxiliaryData auxiliaryData)
        => auxiliaryData.Metadata()?.Count > 0;

    public static bool HasNativeScripts(this AuxiliaryData auxiliaryData)
        => auxiliaryData.NativeScripts()?.Any() == true;

    public static bool HasPlutusScripts(this AuxiliaryData auxiliaryData)
        => auxiliaryData.PlutusV1Scripts()?.Any() == true
           || auxiliaryData.PlutusV2Scripts()?.Any() == true
           || auxiliaryData.PlutusV3Scripts()?.Any() == true;

    public static bool IsShelleyEra(this AuxiliaryData auxiliaryData)
    => auxiliaryData is Metadata;

    public static bool IsShelleyMaEra(this AuxiliaryData auxiliaryData)
        => auxiliaryData is ShellyMaAuxiliaryData;

    public static bool IsPostAlonzoEra(this AuxiliaryData auxiliaryData)
        => auxiliaryData is PostAlonzoAuxiliaryDataMap;
}
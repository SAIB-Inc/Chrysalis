using CMetadata = Chrysalis.Codec.Types.Cardano.Core.Metadata;
using CAuxiliaryData = Chrysalis.Codec.Types.Cardano.Core.IAuxiliaryData;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;

namespace Chrysalis.Codec.Extensions.Cardano.Core;

/// <summary>
/// Extension methods for <see cref="CAuxiliaryData"/> to access metadata and scripts.
/// </summary>
public static class AuxiliaryDataExtensions
{
    /// <summary>
    /// Gets the metadata from the auxiliary data, if present.
    /// </summary>
    /// <param name="self">The auxiliary data instance.</param>
    /// <returns>The metadata, or null if not present.</returns>
    public static CMetadata? Metadata(this CAuxiliaryData self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.TransactionMetadata,
            CMetadata metadata => metadata,
            ShellyMaAuxiliaryData shellyMaAuxiliaryData => shellyMaAuxiliaryData.TransactionMetadata,
            _ => null
        };
    }

    /// <summary>
    /// Gets the native script set from the auxiliary data, if present.
    /// </summary>
    /// <param name="self">The auxiliary data instance.</param>
    /// <returns>The native scripts, or null if not present.</returns>
    public static IEnumerable<INativeScript>? NativeScriptSet(this CAuxiliaryData self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.NativeScripts?.GetValue(),
            ShellyMaAuxiliaryData shellyMaAuxiliaryData => shellyMaAuxiliaryData.AuxiliaryScripts.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus V1 script set from the auxiliary data, if present.
    /// </summary>
    /// <param name="self">The auxiliary data instance.</param>
    /// <returns>The Plutus V1 scripts, or null if not present.</returns>
    public static IEnumerable<ReadOnlyMemory<byte>>? PlutusV1ScriptSet(this CAuxiliaryData self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.PlutusV1Scripts?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus V2 script set from the auxiliary data, if present.
    /// </summary>
    /// <param name="self">The auxiliary data instance.</param>
    /// <returns>The Plutus V2 scripts, or null if not present.</returns>
    public static IEnumerable<ReadOnlyMemory<byte>>? PlutusV2ScriptSet(this CAuxiliaryData self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.PlutusV2Scripts?.GetValue(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the Plutus V3 script set from the auxiliary data, if present.
    /// </summary>
    /// <param name="self">The auxiliary data instance.</param>
    /// <returns>The Plutus V3 scripts, or null if not present.</returns>
    public static IEnumerable<ReadOnlyMemory<byte>>? PlutusV3ScriptSet(this CAuxiliaryData self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.PlutusV3Scripts?.GetValue(),
            _ => null
        };
    }
}

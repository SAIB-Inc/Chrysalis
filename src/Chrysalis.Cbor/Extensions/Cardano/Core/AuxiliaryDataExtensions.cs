using CMetadata = Chrysalis.Cbor.Types.Cardano.Core.Metadata;
using CAuxiliaryData = Chrysalis.Cbor.Types.Cardano.Core.AuxiliaryData;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core;

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
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.MetadataValue,
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
    public static IEnumerable<NativeScript>? NativeScriptSet(this CAuxiliaryData self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.NativeScriptSet?.Value,
            ShellyMaAuxiliaryData shellyMaAuxiliaryData => shellyMaAuxiliaryData.AuxiliaryScripts.Value,
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
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.PlutusV1ScriptSet?.Value,
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
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.PlutusV2ScriptSet?.Value,
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
            PostAlonzoAuxiliaryDataMap postAlonzoAuxiliaryDataMap => postAlonzoAuxiliaryDataMap.PlutusV3ScriptSet?.Value,
            _ => null
        };
    }
}

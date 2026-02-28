using CPlutusData = Chrysalis.Cbor.Types.Cardano.Core.Common.PlutusData;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Common;

/// <summary>
/// Extension methods for <see cref="CPlutusData"/> to access raw bytes.
/// </summary>
public static class PlutusDataExtensions
{
    /// <summary>
    /// Gets the raw CBOR bytes of the Plutus data.
    /// </summary>
    /// <param name="self">The Plutus data instance.</param>
    /// <returns>The raw bytes, or an empty array if not available.</returns>
    public static byte[] Raw(this CPlutusData self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Raw?.ToArray() ?? [];
    }
}

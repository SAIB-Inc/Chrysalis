using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;

/// <summary>
/// Extension methods for <see cref="Redeemers"/> to convert to list or dictionary form.
/// </summary>
public static class RedeemerExtensions
{
    /// <summary>
    /// Converts the redeemers to a list of redeemer entries.
    /// </summary>
    /// <param name="self">The redeemers instance.</param>
    /// <returns>The read-only list of redeemer entries, or empty if not a list format.</returns>
    public static IReadOnlyList<RedeemerEntry> ToList(this Redeemers self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            RedeemerList list => list.Value,
            _ => Array.Empty<RedeemerEntry>()
        };
    }

    /// <summary>
    /// Converts the redeemers to a dictionary of redeemer key-value pairs.
    /// </summary>
    /// <param name="self">The redeemers instance.</param>
    /// <returns>The dictionary of redeemer entries, or empty if not a map format.</returns>
    public static Dictionary<RedeemerKey, RedeemerValue> ToDict(this Redeemers self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            RedeemerMap map => map.Value,
            _ => []
        };
    }
}

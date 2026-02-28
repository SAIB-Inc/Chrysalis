using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Governance;

/// <summary>
/// Extension methods for <see cref="GovActionId"/> to access transaction ID and index.
/// </summary>
public static class GovActionIdExtensions
{
    /// <summary>
    /// Gets the transaction ID of the governance action.
    /// </summary>
    /// <param name="self">The governance action ID instance.</param>
    /// <returns>The transaction ID bytes.</returns>
    public static byte[] TransactionId(this GovActionId self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.TransactionId;
    }

    /// <summary>
    /// Gets the governance action index within the transaction.
    /// </summary>
    /// <param name="self">The governance action ID instance.</param>
    /// <returns>The governance action index.</returns>
    public static int GovActionIndex(this GovActionId self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.GovActionIndex;
    }
}

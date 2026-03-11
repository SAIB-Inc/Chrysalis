using Chrysalis.Codec.Types.Cardano.Core.Governance;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Governance;

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
    public static ReadOnlyMemory<byte> TransactionId(this GovActionId self) => self.TransactionId;

    /// <summary>
    /// Gets the governance action index within the transaction.
    /// </summary>
    /// <param name="self">The governance action ID instance.</param>
    /// <returns>The governance action index.</returns>
    public static ulong GovActionIndex(this GovActionId self) => self.GovActionIndex;
}

using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using CVotingProcedure = Chrysalis.Cbor.Types.Cardano.Core.Governance.VotingProcedure;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Governance;

/// <summary>
/// Extension methods for <see cref="CVotingProcedure"/> to access vote and anchor.
/// </summary>
public static class VotingProcedureExtensions
{
    /// <summary>
    /// Gets the vote value from the voting procedure.
    /// </summary>
    /// <param name="self">The voting procedure instance.</param>
    /// <returns>The vote value.</returns>
    public static int Vote(this CVotingProcedure self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Vote;
    }

    /// <summary>
    /// Gets the anchor from the voting procedure, if present.
    /// </summary>
    /// <param name="self">The voting procedure instance.</param>
    /// <returns>The anchor, or null.</returns>
    public static Anchor? Anchor(this CVotingProcedure self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Anchor;
    }
}

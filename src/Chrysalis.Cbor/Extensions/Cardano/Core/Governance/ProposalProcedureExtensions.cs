using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using CProposalProcedure = Chrysalis.Cbor.Types.Cardano.Core.Governance.ProposalProcedure;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Governance;

/// <summary>
/// Extension methods for <see cref="CProposalProcedure"/> to access proposal fields.
/// </summary>
public static class ProposalProcedureExtensions
{
    /// <summary>
    /// Gets the deposit amount for the proposal.
    /// </summary>
    /// <param name="self">The proposal procedure instance.</param>
    /// <returns>The deposit in lovelace.</returns>
    public static ulong Deposit(this CProposalProcedure self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Deposit;
    }

    /// <summary>
    /// Gets the reward account that receives the deposit return.
    /// </summary>
    /// <param name="self">The proposal procedure instance.</param>
    /// <returns>The reward account.</returns>
    public static RewardAccount RewardAccount(this CProposalProcedure self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.RewardAccount;
    }

    /// <summary>
    /// Gets the governance action from the proposal.
    /// </summary>
    /// <param name="self">The proposal procedure instance.</param>
    /// <returns>The governance action.</returns>
    public static GovAction GovAction(this CProposalProcedure self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.GovAction;
    }

    /// <summary>
    /// Gets the anchor from the proposal.
    /// </summary>
    /// <param name="self">The proposal procedure instance.</param>
    /// <returns>The anchor.</returns>
    public static Anchor Anchor(this CProposalProcedure self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Anchor;
    }
}

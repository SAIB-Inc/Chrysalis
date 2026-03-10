using Chrysalis.Codec.V2.Types.Cardano.Core.Governance;

namespace Chrysalis.Codec.V2.Extensions.Cardano.Core.Governance;

/// <summary>
/// Extension methods for <see cref="IGovAction"/> to access governance action properties.
/// </summary>
public static class GovActionExtensions
{
    /// <summary>
    /// Gets the governance action type tag.
    /// </summary>
    /// <param name="self">The governance action instance.</param>
    /// <returns>The action type value.</returns>
    public static int Type(this IGovAction self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ParameterChangeAction parameterChangeAction => parameterChangeAction.Tag,
            HardForkInitiationAction hardForkInitiationAction => hardForkInitiationAction.Tag,
            TreasuryWithdrawalsAction treasuryWithdrawalsAction => treasuryWithdrawalsAction.Tag,
            NoConfidence noConfidence => noConfidence.Tag,
            UpdateCommittee updateCommittee => updateCommittee.Tag,
            NewConstitution newConstitution => newConstitution.Tag,
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Gets the governance action ID referenced by this action, if applicable.
    /// </summary>
    /// <param name="self">The governance action instance.</param>
    /// <returns>The governance action ID, or null.</returns>
    public static GovActionId? GovActionId(this IGovAction self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ParameterChangeAction parameterChangeAction => parameterChangeAction.ActionId,
            HardForkInitiationAction hardForkInitiationAction => hardForkInitiationAction.ActionId,
            NoConfidence noConfidence => noConfidence.ActionId,
            UpdateCommittee updateCommittee => updateCommittee.ActionId,
            NewConstitution newConstitution => newConstitution.ActionId,
            _ => null
        };
    }

    /// <summary>
    /// Gets the policy hash from the governance action, if applicable.
    /// </summary>
    /// <param name="self">The governance action instance.</param>
    /// <returns>The policy hash bytes, or null.</returns>
    public static ReadOnlyMemory<byte>? PolicyHash(this IGovAction self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ParameterChangeAction parameterChangeAction => parameterChangeAction.PolicyHash,
            TreasuryWithdrawalsAction treasuryWithdrawalsAction => treasuryWithdrawalsAction.PolicyHash,
            _ => null
        };
    }
}

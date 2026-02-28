using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Governance;

/// <summary>
/// Extension methods for <see cref="GovAction"/> to access governance action properties.
/// </summary>
public static class GovActionExtensions
{
    /// <summary>
    /// Gets the governance action type tag.
    /// </summary>
    /// <param name="self">The governance action instance.</param>
    /// <returns>The action type value.</returns>
    public static int Type(this GovAction self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ParameterChangeAction parameterChangeAction => parameterChangeAction.ActionType,
            HardForkInitiationAction hardForkInitiationAction => hardForkInitiationAction.ActionType,
            TreasuryWithdrawalsAction treasuryWithdrawalsAction => treasuryWithdrawalsAction.ActionType,
            NoConfidence noConfidence => noConfidence.ActionType,
            UpdateCommittee updateCommittee => updateCommittee.ActionType,
            NewConstitution newConstitution => newConstitution.ActionType,
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Gets the governance action ID referenced by this action, if applicable.
    /// </summary>
    /// <param name="self">The governance action instance.</param>
    /// <returns>The governance action ID, or null.</returns>
    public static GovActionId? GovActionId(this GovAction self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ParameterChangeAction parameterChangeAction => parameterChangeAction.GovActionId,
            HardForkInitiationAction hardForkInitiationAction => hardForkInitiationAction.GovActionId,
            NoConfidence noConfidence => noConfidence.GovActionId,
            UpdateCommittee updateCommittee => updateCommittee.GovActionId,
            NewConstitution newConstitution => newConstitution.GovActionId,
            _ => null
        };
    }

    /// <summary>
    /// Gets the policy hash from the governance action, if applicable.
    /// </summary>
    /// <param name="self">The governance action instance.</param>
    /// <returns>The policy hash bytes, or null.</returns>
    public static byte[]? PolicyHash(this GovAction self)
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

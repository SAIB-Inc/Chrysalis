using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Governance;

public static class GovActionExtensions
{
    public static int Type(this GovAction self) =>
        self switch
        {
            ParameterChangeAction parameterChangeAction => parameterChangeAction.ActionType,
            HardForkInitiationAction hardForkInitiationAction => hardForkInitiationAction.ActionType,
            TreasuryWithdrawalsAction treasuryWithdrawalsAction => treasuryWithdrawalsAction.ActionType,
            NoConfidence noConfidence => noConfidence.ActionType,
            UpdateCommittee updateCommittee => updateCommittee.ActionType,
            NewConstitution newConstitution => newConstitution.ActionType,
            _ => throw new NotImplementedException()
        };

    public static GovActionId? GovActionId(this GovAction self) => 
        self switch
        {
            ParameterChangeAction parameterChangeAction => parameterChangeAction.GovActionId,
            HardForkInitiationAction hardForkInitiationAction => hardForkInitiationAction.GovActionId,
            NoConfidence noConfidence => noConfidence.GovActionId,
            UpdateCommittee updateCommittee => updateCommittee.GovActionId,
            NewConstitution newConstitution => newConstitution.GovActionId,
            _ => null
        };

    public static byte[]? PolicyHash(this GovAction self) =>
        self switch
        {
            ParameterChangeAction parameterChangeAction => parameterChangeAction.PolicyHash,
            TreasuryWithdrawalsAction treasuryWithdrawalsAction => treasuryWithdrawalsAction.PolicyHash,
            _ => null
        };
}
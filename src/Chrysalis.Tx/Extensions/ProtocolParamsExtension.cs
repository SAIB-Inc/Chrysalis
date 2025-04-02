using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Network.Cbor.LocalStateQuery;

namespace Chrysalis.Tx.Extensions;

public static class ProtocolParamsExtension
{
    public static ConwayProtocolParamUpdate Conway(this ProtocolParams self)
    {
        return new ConwayProtocolParamUpdate(
            self.MinFeeA,
            self.MinFeeB,
            self.MaxBlockBodySize,
            self.MaxTransactionSize,
            self.MaxBlockHeaderSize,
            self.KeyDeposit,
            self.MinPoolCost,
            self.MaximumEpoch,
            self.DesiredNumberOfStakePools,
            self.PoolPledgeInfluence,
            self.ExpansionRate,
            self.TreasuryGrowthRate,
            self.MinPoolCost,
            self.AdaPerUTxOByte,
            self.CostModelsForScriptLanguage,
            self.ExecutionCosts,
            self.MaxTxExUnits,
            self.MaxBlockExUnits,
            self.MaxValueSize,
            self.CollateralPercentage,
            self.MaxCollateralInputs,
            self.PoolVotingThresholds,
            self.DRepVotingThresholds,
            self.MinCommitteeSize,
            self.CommitteeTermLimit,
            self.GovernanceActionValidityPeriod,
            self.GovernanceActionDeposit,
            self.DRepDeposit,
            self.DRepInactivityPeriod,
            self.MinFeeRefScriptCostPerByte
        );

    }
}
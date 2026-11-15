using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Compares cardano-cli parameter JSON with the provider's ledger record.</summary>
internal static class LedgerParameters
{
    internal static void Equal(JsonElement expected, ProtocolParams p) => Equal(expected, new Dictionary<string, object?>
    {
        ["txFeePerByte"] = p.MinFeeA,
        ["txFeeFixed"] = p.MinFeeB,
        ["maxBlockBodySize"] = p.MaxBlockBodySize,
        ["maxTxSize"] = p.MaxTransactionSize,
        ["maxBlockHeaderSize"] = p.MaxBlockHeaderSize,
        ["stakeAddressDeposit"] = p.KeyDeposit,
        ["stakePoolDeposit"] = p.PoolDeposit,
        ["poolRetireMaxEpoch"] = p.MaximumEpoch,
        ["stakePoolTargetNum"] = p.DesiredNumberOfStakePools,
        ["poolPledgeInfluence"] = p.PoolPledgeInfluence,
        ["monetaryExpansion"] = p.ExpansionRate,
        ["treasuryCut"] = p.TreasuryGrowthRate,
        ["protocolVersion"] = new Dictionary<string, object?> { ["major"] = p.ProtocolVersion?.Major, ["minor"] = p.ProtocolVersion?.SequenceNumber },
        ["minPoolCost"] = p.MinPoolCost,
        ["utxoCostPerByte"] = p.AdaPerUTxOByte,
        ["costModels"] = new Dictionary<string, object?>
        {
            ["PlutusV1"] = p.CostModelsForScriptLanguage?.PlutusV1?.GetValue().ToArray(),
            ["PlutusV2"] = p.CostModelsForScriptLanguage?.PlutusV2?.GetValue().ToArray(),
            ["PlutusV3"] = p.CostModelsForScriptLanguage?.PlutusV3?.GetValue().ToArray()
        },
        ["executionUnitPrices"] = new Dictionary<string, object?> { ["priceMemory"] = p.ExecutionCosts?.MemPrice, ["priceSteps"] = p.ExecutionCosts?.StepPrice },
        ["maxTxExecutionUnits"] = new Dictionary<string, object?> { ["memory"] = p.MaxTxExUnits?.Mem, ["steps"] = p.MaxTxExUnits?.Steps },
        ["maxBlockExecutionUnits"] = new Dictionary<string, object?> { ["memory"] = p.MaxBlockExUnits?.Mem, ["steps"] = p.MaxBlockExUnits?.Steps },
        ["maxValueSize"] = p.MaxValueSize,
        ["collateralPercentage"] = p.CollateralPercentage,
        ["maxCollateralInputs"] = p.MaxCollateralInputs,
        ["poolVotingThresholds"] = new Dictionary<string, object?>
        {
            ["motionNoConfidence"] = p.PoolVotingThresholds?.MotionNoConfidence,
            ["committeeNormal"] = p.PoolVotingThresholds?.CommitteeNormal,
            ["committeeNoConfidence"] = p.PoolVotingThresholds?.CommitteeNoConfidence,
            ["hardForkInitiation"] = p.PoolVotingThresholds?.HardForkInitiation,
            ["ppSecurityGroup"] = p.PoolVotingThresholds?.SecurityRelevantThreshold
        },
        ["dRepVotingThresholds"] = new Dictionary<string, object?>
        {
            ["motionNoConfidence"] = p.DRepVotingThresholds?.MotionNoConfidence,
            ["committeeNormal"] = p.DRepVotingThresholds?.CommitteeNormal,
            ["committeeNoConfidence"] = p.DRepVotingThresholds?.CommitteeNoConfidence,
            ["updateToConstitution"] = p.DRepVotingThresholds?.UpdateConstitution,
            ["hardForkInitiation"] = p.DRepVotingThresholds?.HardForkInitiation,
            ["ppNetworkGroup"] = p.DRepVotingThresholds?.PPNetworkGroup,
            ["ppEconomicGroup"] = p.DRepVotingThresholds?.PPEconomicGroup,
            ["ppTechnicalGroup"] = p.DRepVotingThresholds?.PPTechnicalGroup,
            ["ppGovGroup"] = p.DRepVotingThresholds?.PPGovernanceGroup,
            ["treasuryWithdrawal"] = p.DRepVotingThresholds?.TreasuryWithdrawal
        },
        ["committeeMinSize"] = p.MinCommitteeSize,
        ["committeeMaxTermLength"] = p.CommitteeTermLimit,
        ["govActionLifetime"] = p.GovernanceActionValidityPeriod,
        ["govActionDeposit"] = p.GovernanceActionDeposit,
        ["dRepDeposit"] = p.DRepDeposit,
        ["dRepActivity"] = p.DRepInactivityPeriod,
        ["minFeeRefScriptCostPerByte"] = p.MinFeeRefScriptCostPerByte
    });

    private static void Equal(JsonElement expected, object? actual)
    {
        Assert.NotNull(actual);
        if (expected.ValueKind == JsonValueKind.Object)
        {
            Dictionary<string, object?> fields = Assert.IsType<Dictionary<string, object?>>(actual);
            foreach (JsonProperty property in expected.EnumerateObject())
            {
                Assert.True(fields.TryGetValue(property.Name, out object? value), $"Unknown parameter {property.Name}.");
                Equal(property.Value, value);
            }
        }
        else if (expected.ValueKind == JsonValueKind.Array)
        {
            Assert.Equal(expected.EnumerateArray().Select(item => item.GetInt64()), Assert.IsAssignableFrom<IEnumerable<long>>(actual));
        }
        else if (actual is CborRationalNumber rational)
        {
            BigInteger numerator;
            BigInteger denominator;
            if (expected.ValueKind == JsonValueKind.String)
            {
                string[] fraction = expected.GetString()!.Split('/');
                numerator = BigInteger.Parse(fraction[0], CultureInfo.InvariantCulture);
                denominator = BigInteger.Parse(fraction[1], CultureInfo.InvariantCulture);
            }
            else
            {
                int[] bits = decimal.GetBits(expected.GetDecimal());
                numerator = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
                numerator = bits[3] < 0 ? -numerator : numerator;
                denominator = BigInteger.Pow(10, (bits[3] >> 16) & 0xff);
            }

            Assert.NotEqual(BigInteger.Zero, denominator);
            Assert.Equal(numerator * rational.Denominator, (BigInteger)rational.Numerator * denominator);
        }
        else
        {
            Assert.Equal(expected.GetUInt64(), Assert.IsType<ulong>(actual));
        }
    }
}

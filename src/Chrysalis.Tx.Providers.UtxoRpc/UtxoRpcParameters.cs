using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Google.Protobuf.Collections;
using Spec = Utxorpc.V1Beta.Cardano;

namespace Chrysalis.Tx.Providers;

/// <summary>Maps the server's parameter values without narrowing or rounding.</summary>
internal static class UtxoRpcParameters
{
    internal static ProtocolParams Map(Spec.PParams p) => new(
        Integer(p.MinFeeCoefficient, "min_fee_coefficient"),
        Integer(p.MinFeeConstant, "min_fee_constant"),
        p.MaxBlockBodySize,
        p.MaxTxSize,
        p.MaxBlockHeaderSize,
        Integer(p.StakeKeyDeposit, "stake_key_deposit"),
        Integer(p.PoolDeposit, "pool_deposit"),
        p.PoolRetirementEpochBound == 0 ? null : p.PoolRetirementEpochBound,
        p.DesiredNumberOfPools,
        Rational(p.PoolInfluence, "pool_influence"),
        Rational(p.MonetaryExpansion, "monetary_expansion"),
        Rational(p.TreasuryExpansion, "treasury_expansion"),
        p.ProtocolVersion is { } v ? ProtocolVersion.Create(v.Major, v.Minor) : null,
        Integer(p.MinPoolCost, "min_pool_cost"),
        Integer(p.CoinsPerUtxoByte, "coins_per_utxo_byte"),
        p.CostModels is { } c ? new CostMdls(CostModel(c.PlutusV1), CostModel(c.PlutusV2), CostModel(c.PlutusV3)) : null,
        p.Prices is { } prices ? ExUnitPrices.Create(
            Ratio(prices.Memory ?? throw new InvalidDataException("prices.memory is missing."), "prices.memory"),
            Ratio(prices.Steps ?? throw new InvalidDataException("prices.steps is missing."), "prices.steps")) : null,
        p.MaxExecutionUnitsPerTransaction is { } tx ? ExUnits.Create(tx.Memory, tx.Steps) : null,
        p.MaxExecutionUnitsPerBlock is { } block ? ExUnits.Create(block.Memory, block.Steps) : null,
        p.MaxValueSize,
        p.CollateralPercentage,
        p.MaxCollateralInputs,
        PoolThresholds(p.PoolVotingThresholds),
        DRepThresholds(p.DrepVotingThresholds),
        p.MinCommitteeSize,
        p.CommitteeTermLimit,
        p.GovernanceActionValidityPeriod,
        Integer(p.GovernanceActionDeposit, "governance_action_deposit"),
        Integer(p.DrepDeposit, "drep_deposit"),
        p.DrepInactivityPeriod,
        Rational(p.MinFeeScriptRefCostPerByte, "min_fee_script_ref_cost_per_byte"));

    private static ulong? Integer(Spec.BigInt? value, string field)
    {
        if (value is null)
        {
            return null;
        }

        if (value is { BigIntCase: Spec.BigInt.BigIntOneofCase.Int, Int: >= 0 })
        {
            return (ulong)value.Int;
        }

        ReadOnlySpan<byte> bytes = value.BigUInt.Span.TrimStart((byte)0);
        if (value.BigIntCase != Spec.BigInt.BigIntOneofCase.BigUInt || bytes.Length > sizeof(ulong))
        {
            throw new InvalidDataException($"{field} must be an unsigned 64-bit integer.");
        }

        ulong number = 0;
        foreach (byte b in bytes)
        {
            number = (number << 8) | b;
        }

        return number;
    }

    private static CborRationalNumber? Rational(Spec.RationalNumber? value, string field) =>
        value is null ? null : Ratio(value, field);

    private static CborRationalNumber Ratio(Spec.RationalNumber value, string field) =>
        value.Numerator < 0 || value.Denominator == 0
            ? throw new InvalidDataException($"{field} requires a nonnegative numerator and a positive denominator.")
            : CborRationalNumber.Create((ulong)value.Numerator, value.Denominator);

    private static CborDefList<long>? CostModel(Spec.CostModel? value) =>
        value is { Values.Count: > 0 } ? CborDefList<long>.Create([.. value.Values]) : null;

    private static RepeatedField<Spec.RationalNumber>? Thresholds(Spec.VotingThresholds? value, int count, string field) =>
        value is null || value.Thresholds.Count == 0 ? null
            : value.Thresholds.Count == count ? value.Thresholds
            : throw new InvalidDataException($"{field} requires exactly {count} thresholds.");

    private static PoolVotingThresholds? PoolThresholds(Spec.VotingThresholds? value)
    {
        const string field = "pool_voting_thresholds";
        return Thresholds(value, 5, field) is { } t
            ? PoolVotingThresholds.Create(Ratio(t[0], field), Ratio(t[1], field), Ratio(t[2], field), Ratio(t[3], field), Ratio(t[4], field))
            : null;
    }

    private static DRepVotingThresholds? DRepThresholds(Spec.VotingThresholds? value)
    {
        const string field = "drep_voting_thresholds";
        return Thresholds(value, 10, field) is { } t
            ? DRepVotingThresholds.Create(Ratio(t[0], field), Ratio(t[1], field), Ratio(t[2], field), Ratio(t[3], field), Ratio(t[4], field),
                Ratio(t[5], field), Ratio(t[6], field), Ratio(t[7], field), Ratio(t[8], field), Ratio(t[9], field))
            : null;
    }
}

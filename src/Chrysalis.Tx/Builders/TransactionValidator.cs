using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Pre-build validation for transaction builders.
/// Catches common errors before serialization.
/// </summary>
public static class TransactionValidator
{
    /// <summary>
    /// Validates the current state of the transaction builder and returns any issues found.
    /// </summary>
    public static List<string> Validate(TransactionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        List<string> issues = [];

        CheckDuplicateInputs(builder, issues);
        CheckOutputsMinAda(builder, issues);
        CheckCollateralForScripts(builder, issues);
        CheckFeeSet(builder, issues);

        return issues;
    }

    private static void CheckDuplicateInputs(TransactionBuilder builder, List<string> issues)
    {
        HashSet<string> seen = [];
        foreach (TransactionInput input in builder.Inputs)
        {
            string key = Convert.ToHexString(input.TransactionId.Span) + "#" + input.Index;
            if (!seen.Add(key))
            {
                issues.Add($"Duplicate input: {key}");
            }
        }
    }

    private static void CheckOutputsMinAda(TransactionBuilder builder, List<string> issues)
    {
        if (builder.Pparams?.AdaPerUTxOByte is null)
        {
            return;
        }

        ulong adaPerByte = (ulong)builder.Pparams.AdaPerUTxOByte;
        for (int i = 0; i < builder.Outputs.Count; i++)
        {
            byte[] outputBytes = CborSerializer.Serialize(builder.Outputs[i]);
            ulong minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerByte, outputBytes);
            ulong actualLovelace = builder.Outputs[i].Amount().Lovelace();

            if (actualLovelace < minLovelace)
            {
                issues.Add($"Output [{i}] has {actualLovelace} lovelace but minimum is {minLovelace}");
            }
        }
    }

    private static void CheckCollateralForScripts(TransactionBuilder builder, List<string> issues)
    {
        bool hasScripts = builder.Redeemers is not null || builder.RedeemerSet.HasRedeemers;
        bool hasCollateral = builder.Collateral is not null && builder.Collateral.Count > 0;

        if (hasScripts && !hasCollateral)
        {
            issues.Add("Transaction uses scripts but no collateral inputs are provided");
        }
    }

    private static void CheckFeeSet(TransactionBuilder builder, List<string> issues)
    {
        if (builder.Fee == 0)
        {
            issues.Add("Fee is not set (still 0)");
        }
    }
}

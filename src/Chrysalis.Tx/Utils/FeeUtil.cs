using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Utility methods for calculating Cardano transaction fees.
/// </summary>
public static class FeeUtil
{
    /// <summary>
    /// Hardcoded size increment for tiered reference script pricing.
    /// </summary>
    private const int SizeIncrement = 25600;

    /// <summary>
    /// Hardcoded multiplier for tiered reference script pricing.
    /// </summary>
    private const double Multiplier = 1.2;

    /// <summary>
    /// Protocol overhead bytes added to output size for minimum lovelace calculation.
    /// </summary>
    private const ulong PROTOCOL_OVERHEAD_BYTES = 160;

    /// <summary>
    /// Calculates tiered reference script fee based on total script size.
    /// </summary>
    /// <param name="totalScriptSize">Total size of all reference scripts in bytes.</param>
    /// <param name="minFeeRefScriptCostPerByte">Cost per byte from protocol parameters.</param>
    /// <returns>The calculated reference script fee in lovelace.</returns>
    public static ulong CalculateReferenceScriptFee(int totalScriptSize, ulong minFeeRefScriptCostPerByte)
    {
        double accumulatedFee = 0;
        double currentTierPrice = minFeeRefScriptCostPerByte;
        int remainingSize = totalScriptSize;

        while (remainingSize > 0)
        {
            if (remainingSize <= SizeIncrement)
            {
                accumulatedFee += remainingSize * currentTierPrice;
                break;
            }
            else
            {
                accumulatedFee += SizeIncrement * currentTierPrice;
                currentTierPrice *= Multiplier;
                remainingSize -= SizeIncrement;
            }
        }

        return (ulong)Math.Floor(accumulatedFee);
    }

    /// <summary>
    /// Calculates tiered reference script fee from raw script bytes.
    /// </summary>
    /// <param name="refScriptBytes">The reference script bytes.</param>
    /// <param name="minFeeRefScriptCostPerByte">Cost per byte from protocol parameters.</param>
    /// <returns>The calculated reference script fee in lovelace.</returns>
    public static ulong CalculateReferenceScriptFee(byte[] refScriptBytes, ulong minFeeRefScriptCostPerByte)
    {
        ArgumentNullException.ThrowIfNull(refScriptBytes);
        return CalculateReferenceScriptFee(refScriptBytes.Length, minFeeRefScriptCostPerByte);
    }

    /// <summary>
    /// Calculates the base transaction fee from size and protocol parameters.
    /// </summary>
    /// <param name="txSizeInBytes">Transaction size in bytes.</param>
    /// <param name="minFeeA">Linear fee coefficient (per-byte fee).</param>
    /// <param name="minFeeB">Constant fee component.</param>
    /// <returns>The calculated fee in lovelace.</returns>
    public static ulong CalculateFee(ulong txSizeInBytes, ulong minFeeA, ulong minFeeB)
    {
        return txSizeInBytes <= 0
            ? throw new ArgumentException("Transaction size must be greater than 0", nameof(txSizeInBytes))
            : (minFeeA * txSizeInBytes) + minFeeB;
    }

    /// <summary>
    /// Calculates fee including additional witness signature overhead.
    /// </summary>
    /// <param name="txSizeInBytes">Transaction size in bytes.</param>
    /// <param name="minFeeA">Linear fee coefficient (per-byte fee).</param>
    /// <param name="minFeeB">Constant fee component.</param>
    /// <param name="numberOfSignatures">Number of additional signatures to account for.</param>
    /// <returns>The calculated fee in lovelace.</returns>
    public static ulong CalculateFeeWithWitness(ulong txSizeInBytes, ulong minFeeA, ulong minFeeB, int numberOfSignatures = 0)
    {
        if (txSizeInBytes <= 0)
        {
            throw new ArgumentException("Transaction size must be greater than 0", nameof(txSizeInBytes));
        }

        ulong totalTxSize = txSizeInBytes + (ulong)(numberOfSignatures * 142);

        return CalculateFee(totalTxSize, minFeeA, minFeeB);
    }

    /// <summary>
    /// Calculates the script execution fee from redeemers and unit prices.
    /// </summary>
    /// <param name="redeemers">The transaction redeemers.</param>
    /// <param name="exUnitPriceStep">Step price as a rational number.</param>
    /// <param name="exUnitPriceMem">Memory price as a rational number.</param>
    /// <returns>The calculated script execution fee in lovelace.</returns>
    public static ulong CalculateScriptExecutionFee(Redeemers redeemers, RationalNumber exUnitPriceStep, RationalNumber exUnitPriceMem)
    {
        if (redeemers is null)
        {
            return 0;
        }

        ArgumentNullException.ThrowIfNull(exUnitPriceStep);
        ArgumentNullException.ThrowIfNull(exUnitPriceMem);

        List<ExUnits> exUnits = redeemers switch
        {
            RedeemerList redeemerList => [.. redeemerList.Value.Select(x => x.ExUnits)],
            RedeemerMap redeemerMap => [.. redeemerMap.Value.Select(x => x.Value.ExUnits)],
            _ => throw new ArgumentException("Invalid redeemers type", nameof(redeemers))
        };

        ulong scriptFee = 0;
        exUnits.ForEach(exUnit =>
        {
            ulong memCost = exUnit.Mem * exUnitPriceStep.Numerator / exUnitPriceStep.Denominator;
            ulong stepCost = exUnit.Steps * exUnitPriceMem.Numerator / exUnitPriceMem.Denominator;

            scriptFee += memCost + stepCost;
        });

        return scriptFee;
    }

    /// <summary>
    /// Calculates the minimum ADA requirement for a transaction output.
    /// </summary>
    /// <param name="utxoCostPerByte">Protocol parameter for cost per byte (lovelace).</param>
    /// <param name="cborBytes">CBOR serialized transaction output.</param>
    /// <returns>Minimum required ADA in lovelace.</returns>
    public static ulong CalculateMinimumLovelace(ulong utxoCostPerByte, byte[] cborBytes)
    {
        ArgumentNullException.ThrowIfNull(cborBytes);
        ulong outputSize = (ulong)cborBytes.Length;
        return utxoCostPerByte * (outputSize + PROTOCOL_OVERHEAD_BYTES);
    }

    /// <summary>
    /// Calculates the required collateral amount based on fee and collateral percentage.
    /// </summary>
    /// <param name="fee">The transaction fee in lovelace.</param>
    /// <param name="collateralPercentage">The collateral percentage from protocol parameters.</param>
    /// <returns>The required collateral in lovelace.</returns>
    public static ulong CalculateRequiredCollateral(ulong fee, ulong collateralPercentage)
    {
        return (ulong)Math.Ceiling((decimal)fee * collateralPercentage / 100);
    }
}

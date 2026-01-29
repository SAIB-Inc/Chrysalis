using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Utils;

public static class FeeUtil
{
    private const int SIGNATURE_SIZE = 96; // Size of Ed25519 signature in bytes
    private const int CBOR_OVERHEAD_PER_SIGNATURE = 10; // CBOR encoding overhead per signature
    private const int CBOR_OVERHEAD_FOR_SIGNATURES = 2;
    private const ulong MINIMUM_UTXO_LOVELACE = 840_499;
    private const ulong PROTOCOL_OVERHEAD_BYTES = 160;

    // hardcoded for now but will be part of the next era after conway
    private const int SizeIncrement = 25600;
    private const double Multiplier = 1.2;

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

    public static ulong CalculateReferenceScriptFee(byte[] refScriptBytes, ulong minFeeRefScriptCostPerByte)
        => CalculateReferenceScriptFee(refScriptBytes.Length, minFeeRefScriptCostPerByte);

    public static ulong CalculateFee(ulong txSizeInBytes, ulong minFeeA, ulong minFeeB)
    {
        if (txSizeInBytes <= 0)
            throw new ArgumentException("Transaction size must be greater than 0", nameof(txSizeInBytes));

        return minFeeA * txSizeInBytes + minFeeB;
    }

    public static ulong CalculateFeeWithWitness(ulong txSizeInBytes, ulong minFeeA, ulong minFeeB, int numberOfSignatures = 0)
    {
        if (txSizeInBytes <= 0)
            throw new ArgumentException("Transaction size must be greater than 0", nameof(txSizeInBytes));

        ulong totalTxSize = txSizeInBytes + (ulong)(numberOfSignatures * 142);

        return CalculateFee(totalTxSize, minFeeA, minFeeB);
    }

    public static ulong CalculateScriptExecutionFee(Redeemers redeemers, RationalNumber exUnitPriceStep, RationalNumber exUnitPriceMem)
    {
        if (redeemers is null) return 0;

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

        if (scriptFee <= 0) return 0;

        return scriptFee;
    }

    /// <summary>
    /// Calculates the minimum ADA requirement for a transaction output
    /// </summary>
    /// <param name="utxoCostPerByte">Protocol parameter for cost per byte (lovelace)</param>
    /// <param name="cborBytes">CBOR serialized transaction output</param>
    /// <returns>Minimum required ADA in lovelace</returns>
    public static ulong CalculateMinimumLovelace(ulong utxoCostPerByte, byte[] cborBytes)
    {
        ulong outputSize = (ulong)cborBytes.Length;
        return utxoCostPerByte * (outputSize + PROTOCOL_OVERHEAD_BYTES);
    }

    public static ulong CalculateRequiredCollateral(ulong fee, ulong collateralPercentage)
    {
        return (ulong)Math.Ceiling((decimal)fee * collateralPercentage / 100);
    }
}
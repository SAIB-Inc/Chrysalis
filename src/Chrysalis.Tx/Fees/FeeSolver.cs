using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Tx.Fees;

internal static class FeeSolver
{
    internal static FeeDraft Prepare(PostMaryTransaction draft, ProtocolParams pp, in FeeInputs inputs, ulong? witnessLength = null)
    {
        if (inputs.ChangeIndex is not null && inputs.Budget is null)
        {
            throw new ArgumentException("A change output requires a spending budget.", nameof(inputs));
        }
        if (inputs.ChangeIndex is null && inputs.Budget is { } budget && inputs.FixedFee is { } fee && fee != budget)
        {
            throw new ArgumentException("Without change the fee must consume the spending budget.", nameof(inputs));
        }

        ITransactionOutput? collateralReturn = inputs.CollateralBalance.HasValue ? draft.Body.CollateralChange() : null;
        if (inputs.ReturnPresent != (collateralReturn is not null)
            || (inputs.ReturnPresent && draft.Body.TotalCollateral() is null))
        {
            throw new ArgumentException("A return requires a matching output, total and collateral balance.", nameof(inputs));
        }

        (ulong feeWidth, ulong totalWidth) = DraftSize.BodyWidths(draft.Body, inputs.ReturnPresent);
        ulong fixedLength = checked(DraftSize.EncodedLength(draft.Body) - feeWidth);
        ulong changeBase = 0;
        if (inputs.ChangeIndex is { } index)
        {
            ITransactionOutput change = DraftSize.ChangeAt(draft.Body, index);
            ulong width = DraftSize.CoinWidth(change.Amount());
            fixedLength = checked(fixedLength - width);
            changeBase = checked(DraftSize.EncodedLength(change) - width);
        }
        ulong returnBase = 0;
        if (collateralReturn is not null)
        {
            ulong width = DraftSize.CoinWidth(collateralReturn.Amount());
            fixedLength = checked(fixedLength - width - totalWidth);
            returnBase = checked(DraftSize.EncodedLength(collateralReturn) - width);
        }

        IRedeemers? redeemers = draft.Witnesses switch
        {
            PostAlonzoTransactionWitnessSet witness => witness.Redeemers,
            AlonzoTransactionWitnessSet witness => witness.Redeemers,
            _ => null
        };
        UInt128 constant = checked(FeeEngine.Parameter(pp.MinFeeB, nameof(pp.MinFeeB)) + FeeEngine.ScriptFeeWide(redeemers, pp)
            + FeeEngine.ReferenceScriptFeeWide(inputs.ReferenceScriptSize, pp) + inputs.Padding);
        return new FeeDraft(inputs, fixedLength, DraftSize.LedgerOverhead(draft, inputs.SigningDelta, witnessLength),
            FeeEngine.Parameter(pp.MinFeeA, nameof(pp.MinFeeA)), constant,
            inputs.CollateralBalance.HasValue ? FeeEngine.Parameter(pp.CollateralPercentage, nameof(pp.CollateralPercentage)) : 0,
            inputs.ChangeIndex.HasValue || inputs.ReturnPresent ? FeeEngine.Parameter(pp.AdaPerUTxOByte, nameof(pp.AdaPerUTxOByte)) : 0,
            changeBase, returnBase);
    }

    internal static FeeResult Solve(in FeeDraft draft)
    {
        FeeInputs inputs = draft.Inputs;
        if ((inputs.FixedFee ?? (inputs.ChangeIndex is null ? inputs.Budget : null)) is { } singleton)
        {
            return Validate(draft, singleton, out FeeResult result, out UInt128 required)
                ? result : Failure(draft, result.Status, required, singleton);
        }

        // Clamp even with no spending budget: floor(100 C / pct) itself may exceed ulong.
        UInt128 collateralMax = inputs.CollateralBalance is { } balance && draft.CollateralPercentage != 0
            ? (UInt128)100 * balance / draft.CollateralPercentage : ulong.MaxValue;
        ulong max = (ulong)UInt128.Min(inputs.Budget ?? ulong.MaxValue, collateralMax);
        if (!inputs.ChangeIndex.HasValue && !inputs.ReturnPresent)
        {
            // Only the fee width varies. Jump over fees known too low before testing the candidate.
            ulong fee = FeeAtWidestOutputs(draft, 0);
            return Validate(draft, fee, out FeeResult result, out _)
                ? result : Failure(draft, FeeStatus.Balanced, 0, 0);
        }

        ulong start = 0;
        while (true)
        {
            CoinWidths widths = WidthsAt(draft, start);
            UInt128 candidate = UInt128.Max(start, Required(draft, BodyLength(draft, widths)));
            // The smallest candidate in a region is the easiest on both budgets. Test it at its own widths.
            if (candidate <= max && WidthsAt(draft, (ulong)candidate) == widths
                && Validate(draft, (ulong)candidate, out FeeResult result, out _))
            {
                return result;
            }

            ulong end = RegionEnd(draft, start, max, widths);
            if (end == max)
            {
                return Failure(draft, FeeStatus.Balanced, 0, 0);
            }
            start = end + 1;
        }
    }

    private static bool Validate(in FeeDraft draft, ulong fee, out FeeResult result, out UInt128 required)
    {
        FeeInputs inputs = draft.Inputs;
        FeeStatus status = FeeStatus.Balanced;
        UInt128 total = ExactFee.Collateral(fee, draft.CollateralPercentage);
        if (inputs.Budget is { } budget && budget < fee)
        {
            status |= FeeStatus.SpendingShort;
        }
        if (inputs.CollateralBalance is { } balance && balance < total)
        {
            status |= FeeStatus.CollateralShort;
        }
        if (status != FeeStatus.Balanced)
        {
            result = new FeeResult(status);
            required = 0;
            return false;
        }

        Coins coins = CoinsAt(draft, fee, total);
        CoinWidths widths = Widths(draft, fee, coins);
        ulong bodyLength = BodyLength(draft, widths);
        required = Required(draft, bodyLength);
        if (required > fee)
        {
            status |= inputs.FixedFee.HasValue ? FeeStatus.FixedFeeTooLow : FeeStatus.SpendingShort;
        }
        if (inputs.ChangeIndex.HasValue && inputs.Budget!.Value < (UInt128)fee + OutputMinimum(draft, draft.ChangeBaseLength, widths.Change))
        {
            status |= FeeStatus.SpendingShort;
        }
        if (inputs.ReturnPresent && inputs.CollateralBalance!.Value < total + OutputMinimum(draft, draft.ReturnBaseLength, widths.Return))
        {
            status |= FeeStatus.CollateralShort;
        }
        result = status == FeeStatus.Balanced
            ? new FeeResult(status, fee, coins.Change, coins.Total, coins.Return, bodyLength,
                checked(bodyLength + draft.LedgerOverhead), inputs.SigningDelta)
            : new FeeResult(status);
        return status == FeeStatus.Balanced;
    }

    private static ulong RegionEnd(in FeeDraft draft, ulong start, ulong max, CoinWidths widths)
    {
        // Each width is monotone (though not all in the same direction), so equality is a prefix.
        ulong lo = start;
        ulong hi = max;
        while (lo < hi)
        {
            ulong distance = hi - lo;
            ulong mid = lo + (distance / 2) + (distance % 2);
            if (WidthsAt(draft, mid) == widths)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return lo;
    }

    private static Coins CoinsAt(in FeeDraft draft, ulong fee, UInt128 total) => new(
        draft.Inputs.ChangeIndex.HasValue ? draft.Inputs.Budget!.Value - fee : 0,
        draft.Inputs.ReturnPresent ? (ulong)total : draft.Inputs.CollateralBalance ?? 0,
        draft.Inputs.ReturnPresent ? draft.Inputs.CollateralBalance!.Value - (ulong)total : 0);

    private static CoinWidths WidthsAt(in FeeDraft draft, ulong fee) =>
        Widths(draft, fee, CoinsAt(draft, fee, ExactFee.Collateral(fee, draft.CollateralPercentage)));

    private static CoinWidths Widths(in FeeDraft draft, ulong fee, Coins coins) => new(DraftSize.Width(fee),
        draft.Inputs.ChangeIndex.HasValue ? DraftSize.Width(coins.Change) : 0,
        draft.Inputs.ReturnPresent ? DraftSize.Width(coins.Total) : 0,
        draft.Inputs.ReturnPresent ? DraftSize.Width(coins.Return) : 0);

    private static ulong BodyLength(in FeeDraft draft, CoinWidths widths) =>
        checked(draft.FixedBodyLength + widths.Fee + widths.Change + widths.Total + widths.Return);

    private static UInt128 Required(in FeeDraft draft, ulong bodyLength) => UInt128.Max(draft.Inputs.MinimumFee,
        ExactFee.Linear(checked(bodyLength + draft.LedgerOverhead), draft.FeePerByte, draft.ConstantFee));

    private static UInt128 OutputMinimum(in FeeDraft draft, ulong baseLength, ulong width) =>
        FeeEngine.MinimumOutput(checked(baseLength + width), draft.CoinsPerOutputByte);

    private static ulong FeeAtWidestOutputs(in FeeDraft draft, ulong fee)
    {
        ulong widest = DraftSize.Width(ulong.MaxValue);
        ulong otherWidths = (draft.Inputs.ChangeIndex.HasValue ? widest : 0) + (draft.Inputs.ReturnPresent ? 2 * widest : 0);
        // g+ is monotone, so jumping to its requirement skips only inadmissible fees.
        while (true)
        {
            UInt128 required = Required(draft, checked(draft.FixedBodyLength + otherWidths + DraftSize.Width(fee)));
            if (required <= fee)
            {
                return fee;
            }
            fee = checked((ulong)required);
        }
    }

    private static FeeResult Failure(in FeeDraft draft, FeeStatus hard, UInt128 required, ulong start)
    {
        FeeInputs inputs = draft.Inputs;
        ulong hint = inputs.FixedFee.HasValue ? 0 : FeeAtWidestOutputs(draft, start);
        ulong selectionFee = inputs.FixedFee ?? hint;
        ulong widest = DraftSize.Width(ulong.MaxValue);
        UInt128 spending = (UInt128)selectionFee + (inputs.ChangeIndex.HasValue ? OutputMinimum(draft, draft.ChangeBaseLength, widest) : 0);
        UInt128 collateral = ExactFee.Collateral(selectionFee, draft.CollateralPercentage)
            + (inputs.ReturnPresent ? OutputMinimum(draft, draft.ReturnBaseLength, widest) : 0);
        ulong spendingRequest = inputs.Budget is { } budget ? Deficit(spending, budget) : 0;
        ulong collateralRequest = inputs.CollateralBalance is { } balance ? Deficit(collateral, balance) : 0;
        FeeStatus status = hard;
        if (status == FeeStatus.Balanced)
        {
            status = (spendingRequest > 0 ? FeeStatus.SpendingShort : FeeStatus.Balanced)
                | (collateralRequest > 0 ? FeeStatus.CollateralShort : FeeStatus.Balanced);
            if (status == FeeStatus.Balanced)
            {
                throw new InvalidOperationException("The fee walk missed a feasible fee.");
            }
        }
        return new FeeResult(status, FeeHint: hint, RequiredAtFixedFee: inputs.FixedFee.HasValue ? required : 0,
            SpendingRequest: spendingRequest, CollateralRequest: collateralRequest);
    }

    private static ulong Deficit(UInt128 need, ulong available) => need > available ? (ulong)UInt128.Min(need - available, ulong.MaxValue) : 0;

    internal static void Check(PostMaryTransaction unsignedResult, in FeeResult solved)
    {
        ulong actualBody = DraftSize.EncodedLength(unsignedResult.Body);
        ulong actualSize = checked(actualBody + DraftSize.LedgerOverhead(unsignedResult, solved.SigningDelta));
        if (solved.Status != FeeStatus.Balanced || actualBody != solved.BodyLength || actualSize != solved.LedgerSize)
        {
            throw new InvalidOperationException($"Predicted body/ledger size {solved.BodyLength}/{solved.LedgerSize} but got {actualBody}/{actualSize}.");
        }
    }

    private readonly record struct Coins(ulong Change, ulong Total, ulong Return);
    private readonly record struct CoinWidths(ulong Fee, ulong Change, ulong Total, ulong Return);
}

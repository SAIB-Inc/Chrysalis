namespace Chrysalis.Tx.Fees;

// An unset budget is different from an empty one. The same applies to collateral.
internal readonly record struct FeeInputs(
    int? ChangeIndex = null,
    ulong? Budget = null,
    ulong? FixedFee = null,
    ulong? CollateralBalance = null,
    bool ReturnPresent = false,
    ulong ReferenceScriptSize = 0,
    ulong SigningDelta = 0,
    ulong Padding = 0,
    ulong MinimumFee = 0);

// Lengths exclude the mutable integers. Without a return, total collateral is not mutable.
internal readonly record struct FeeDraft(
    FeeInputs Inputs,
    ulong FixedBodyLength,
    ulong LedgerOverhead,
    ulong FeePerByte,
    UInt128 ConstantFee,
    ulong CollateralPercentage,
    ulong CoinsPerOutputByte = 0,
    ulong ChangeBaseLength = 0,
    ulong ReturnBaseLength = 0);

[Flags]
internal enum FeeStatus
{
    Balanced = 0,
    SpendingShort = 1,
    CollateralShort = 2,
    FixedFeeTooLow = 4
}

// A balanced result contains amounts and lengths; a failure contains diagnostics only.
// Requests are conservative selection hints, never acceptance criteria for the next round.
internal readonly record struct FeeResult(
    FeeStatus Status,
    ulong Fee = 0,
    ulong Change = 0,
    ulong TotalCollateral = 0,
    ulong CollateralReturn = 0,
    ulong BodyLength = 0,
    ulong LedgerSize = 0,
    ulong SigningDelta = 0,
    ulong FeeHint = 0,
    UInt128 RequiredAtFixedFee = default,
    ulong SpendingRequest = 0,
    ulong CollateralRequest = 0);

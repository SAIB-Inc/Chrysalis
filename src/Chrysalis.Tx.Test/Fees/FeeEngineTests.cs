using System.Globalization;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Test;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Fees;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Test.Cli;
using Xunit;
using static Chrysalis.Tx.Test.Cli.CliVectors;

namespace Chrysalis.Tx.Test.Fees;

/// <summary>Checks Conway fee and output rules against the pinned CLI bytes and targeted edge cases.</summary>
public sealed class FeeEngineTests
{
    private static readonly Lazy<CliVectors> Corpus = new(() => Load(CliVectors.Root));
    private static CliVectors Vectors => Corpus.Value;

    /// <summary>Enumerates stable test IDs for the requested verified row kind.</summary>
    public static IEnumerable<TheoryDataRow<string, int>> Rows(string kind) => Vectors.Cases.SelectMany(item => item
        .GetProperty("rows").EnumerateArray().Select((row, index) => (row, index))
        .Where(pair => Text(pair.row, "kind") == kind)
        .Select(pair => new TheoryDataRow<string, int>(Text(item, "id"), pair.index)));

    /// <summary>All balanced transactions and quotes reproduce the CLI quote, including bootstrap-only F01b.</summary>
    [Theory]
    [MemberData(nameof(Rows), "balanced")]
    [MemberData(nameof(Rows), "quote")]
    public void MinFeeMatchesCli(string id, int index)
    {
        JsonElement row = Row(id, index);
        Assert.Equal(Number(row, "q"), Quote(Tx(row), row));
    }

    /// <summary>Each stored boundary candidate obeys its fee or collateral relation.</summary>
    [Theory]
    [MemberData(nameof(Rows), "candidate")]
    public void CandidateRelationMatchesCli(string id, int index)
    {
        JsonElement row = Row(id, index);
        PostMaryTransaction tx = Tx(row);
        ulong quote = Quote(tx, row);
        Assert.Equal(Number(row, "q"), quote);
        ulong fee = tx.Body.Fee();
        switch (Text(row, "relation"))
        {
            case "fee>quote": Assert.True(fee > quote); break;
            case "fee<quote": Assert.True(fee < quote); break;
            case "collateral<required":
                Assert.Equal(quote, fee);
                Assert.True(tx.Body.TotalCollateral()!.Value < FeeEngine.RequiredCollateral(fee, Pp(row).CollateralPercentage!.Value));
                break;
            default: Assert.Fail("Unknown candidate relation"); break;
        }
    }

    /// <summary>F13 rounds the sum of all units' costs once, not each component or redeemer.</summary>
    [Fact]
    public void ScriptFeesRoundOnce()
    {
        JsonElement row = Row("F13");
        PostMaryTransaction tx = Tx(row);
        ulong signingCost = Number(row, "w") * (ulong)OneWitnessDelta(tx) * Pp(row).MinFeeA!.Value;
        ulong expected = Number(row, "q") - signingCost - FeeEngine.LinearFee(LedgerSize(tx), Pp(row));
        Assert.Equal(1_005UL, expected);
        Assert.Equal(expected, FeeEngine.ScriptFee(Redeemers(tx), Pp(row)));
    }

    /// <summary>Resolves both spending and reference inputs, deduplicating outrefs but not identical scripts.</summary>
    [Theory]
    [InlineData("F10", 6UL)]
    [InlineData("F11", 25_700UL)]
    [InlineData("F11b", 800UL)]
    public void ReferenceSizeUsesResolvedOutrefUnion(string id, ulong expected)
    {
        JsonElement row = Row(id);
        PostMaryTransaction tx = Tx(row);
        ResolvedInput[] utxos = CliUtxo.ReadForReferenceScripts(Vectors, Text(row, "utxo"));
        TransactionInput[] inputs = [.. tx.Body.Inputs()];
        TransactionInput[] references = [.. tx.Body.ReferenceInputs() ?? []];
        ulong size = FeeEngine.ReferenceScriptSize(inputs, references, utxos);
        Assert.Equal(Number(row, "s"), size);
        Assert.Equal(expected, size);
        Assert.Equal(size, FeeEngine.ReferenceScriptSize(inputs, references.Concat(inputs), utxos));
        Assert.Equal(Number(row, "q"), FeeEngine.MinFee(tx, Pp(row), checked((int)Number(row, "w")), size));
    }

    /// <summary>A ScriptRef must contain exactly one closed CBOR array.</summary>
    [Theory]
    [InlineData("9F01430102031863FF")]
    [InlineData("820143010203AA")]
    [InlineData("9F0143010203")]
    [InlineData("9F00820180FF00")]
    public void RejectsMalformedScriptRefFraming(string hex)
    {
        PostMaryTransaction seed = Tx(Row("F11"));
        TransactionInput input = Assert.Single(seed.Body.Inputs());
        Address address = new(seed.Body.Outputs().First().Address());
        PostAlonzoTransactionOutput output = PostAlonzoTransactionOutput.Create(address, Lovelace.FromAmount(2_000_000),
            scriptRef: new CborEncodedValue(Convert.FromHexString(hex)));
        Assert.Throws<InvalidDataException>(() => FeeEngine.ReferenceScriptSize([input], [], [new ResolvedInput(input, output)]));
    }

    /// <summary>F12b's fractional price is applied across tiers before a single floor.</summary>
    [Fact]
    public void FractionalReferencePriceUsesAllTiers()
    {
        JsonElement row = Row("F12b", 3);
        PostMaryTransaction tx = Tx(row);
        ulong withoutReference = Number(Row("F01"), "q");
        Assert.Equal(Number(row, "q") - withoutReference, FeeEngine.ReferenceScriptFee(Number(row, "s"), Pp(row)));
        Assert.Equal(Number(row, "q"), Quote(tx, row));
    }

    /// <summary>Preserved list and map outputs reproduce every CLI minimum at their stored width.</summary>
    [Theory]
    [MemberData(nameof(Rows), "min-utxo")]
    public void MinLovelaceMatchesCli(string id, int index)
    {
        JsonElement row = Row(id, index);
        Assert.Equal(Number(row, "minimum"), FeeEngine.MinLovelace(Output(row), Pp(row).AdaPerUTxOByte!.Value));
    }

    /// <summary>Near 2^32 the least valid coin depends on the permitted starting coin.</summary>
    [Theory]
    [InlineData("M24", "M24")]
    [InlineData("M25", "M25")]
    [InlineData("M26", "M26")]
    [InlineData("M27", "M26")]
    public void LeastValidCoinUsesStartingWidth(string id, string fromZero)
    {
        JsonElement row = Row(id);
        ITransactionOutput output = Output(row);
        ulong cost = Pp(row).AdaPerUTxOByte!.Value;
        Assert.Equal(Number(Row(fromZero), "minimum"), FeeEngine.LeastValidCoin(WithCoin(output, 0), cost));
        Assert.Equal(Number(row, "minimum"), FeeEngine.LeastValidCoin(WithCoin(output, Number(row, "startCoin")), cost));
        ulong funded = Number(row, "minimum") + 1;
        Assert.Equal(funded, FeeEngine.LeastValidCoin(WithCoin(output, funded), cost));
    }

    /// <summary>Ordinary vectors and all stored fee/collateral boundaries match their least valid solution.</summary>
    [Theory]
    [InlineData("F01")]
    [InlineData("F09")]
    [InlineData("B1")]
    [InlineData("B2")]
    [InlineData("B3")]
    [InlineData("B4")]
    [InlineData("B5")]
    public void SolveReproducesStoredAmounts(string id)
    {
        JsonElement row = Row(id);
        PostMaryTransaction tx = Tx(row);
        FeeResult result = FeeSolver.Solve(VectorDraft(row));
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(tx.Body.Fee(), result.Fee);
        Assert.Equal(tx.Body.Outputs().Last().Amount().Lovelace(), result.Change);
        Assert.Equal(tx.Body.TotalCollateral() ?? 0, result.TotalCollateral);
        Assert.Equal(Return(tx)?.Amount().Lovelace() ?? 0, result.CollateralReturn);
        Assert.Equal(0UL, result.FeeHint);
        Assert.Equal((UInt128)0, result.RequiredAtFixedFee);
        Assert.Equal(0UL, result.SpendingRequest);
        Assert.Equal(0UL, result.CollateralRequest);
        FeeSolver.Check(tx, result);
    }

    /// <summary>B6 minimizes the fee without putting more than required at risk as collateral.</summary>
    [Fact]
    public void ReturnGapDoesNotOverstateCollateral()
    {
        JsonElement row = Row("B6");
        PostMaryTransaction tx = Tx(row);
        FeeResult result = FeeSolver.Solve(VectorDraft(row));
        ulong fee5 = Number(row, "q");
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(fee5, Quote(tx, row));
        Assert.Equal(fee5 + 88, result.Fee);
        Assert.Equal(((3 * result.Fee) + 1) / 2, result.TotalCollateral);
        Assert.Equal(uint.MaxValue, result.CollateralReturn);
        Assert.Equal((ulong)tx.Body.Raw.Length, result.BodyLength);
        Assert.Equal(LedgerSize(tx) + (ulong)OneWitnessDelta(tx), result.LedgerSize);
    }

    /// <summary>The final unsigned transaction must match both the priced body and the priced signing size.</summary>
    [Fact]
    public void CheckRejectsBodyAndWitnessWidthDrift()
    {
        JsonElement row = Row("F01");
        PostMaryTransaction tx = Tx(row);
        FeeResult result = FeeSolver.Solve(VectorDraft(row));
        FeeSolver.Check(tx, result);
        Assert.Throws<InvalidOperationException>(() => FeeSolver.Check(Tx(Row("B2")), result));
        PostMaryTransaction signed = Tx(Row("F01", 3));
        Assert.Equal(tx.Body.Raw.Length, signed.Body.Raw.Length);
        Assert.Throws<InvalidOperationException>(() => FeeSolver.Check(signed, result));
    }

    /// <summary>A warm B5 solve crosses the 2^32 coin boundary without allocating.</summary>
    [Fact]
    public void WarmSolveAllocatesNothing()
    {
        JsonElement row = Row("B5");
        FeeDraft draft = VectorDraft(row);
        FeeResult result = default;
        void Solve() => result = FeeSolver.Solve(draft);
        long bytes = AllocationAssert.Measure(Solve, Solve);
        Assert.Equal(0, bytes);
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(Tx(row).Body.Fee(), result.Fee);
    }

    /// <summary>A fixed fee already outside the budget is not priced.</summary>
    [Fact]
    public void FixedFeeFailsBoundsBeforeSizing()
    {
        FeeDraft draft = Synthetic(new FeeInputs(ChangeIndex: 0, Budget: 99, FixedFee: 100), a: 1, c: 4_310);
        FeeResult result = FeeSolver.Solve(draft);
        AssertFailure(result, FeeStatus.SpendingShort);
        Assert.Equal((UInt128)0, result.RequiredAtFixedFee);
        Assert.Equal(874_931UL, result.SpendingRequest); // map change at nine bytes, plus one lovelace
        Assert.Equal(0UL, result.FeeHint);
    }

    /// <summary>The fixed fee diagnostic retains its own widths and remains exact beyond ulong.</summary>
    [Fact]
    public void FixedFeeReportsExactRequirementAtItsWidth()
    {
        FeeDraft draft = Synthetic(new FeeInputs(ChangeIndex: 0, Budget: 1_000_000, FixedFee: 100), a: 1, b: 0) with
        {
            FixedBodyLength = 190,
            LedgerOverhead = 10
        };
        FeeResult result = FeeSolver.Solve(draft);
        AssertFailure(result, FeeStatus.FixedFeeTooLow);
        Assert.Equal((UInt128)207, result.RequiredAtFixedFee);
        Assert.Equal(0UL, result.FeeHint);
        Assert.Equal(0UL, result.SpendingRequest);
        FeeResult wide = FeeSolver.Solve(draft with { ConstantFee = ulong.MaxValue });
        AssertFailure(wide, FeeStatus.FixedFeeTooLow);
        Assert.Equal("18446744073709551822", wide.RequiredAtFixedFee.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Fixed fees use the fee actually charged, not the free fee hint, to request collateral.</summary>
    [Fact]
    public void FixedFeeCollateralUsesChargedAmount()
    {
        FeeResult result = FeeSolver.Solve(Synthetic(new FeeInputs(FixedFee: 1_000, CollateralBalance: 1_499)));
        AssertFailure(result, FeeStatus.CollateralShort);
        Assert.Equal(1UL, result.CollateralRequest);
        Assert.Equal(0UL, result.FeeHint);
        Assert.Equal((UInt128)0, result.RequiredAtFixedFee);
    }

    /// <summary>The no-change fee must consume the entire spending budget.</summary>
    [Fact]
    public void FixedFeeWithoutChangeMustConserveBudget() => Assert.Throws<ArgumentException>(() =>
        Synthetic(new FeeInputs(Budget: 99, FixedFee: 100)));

    /// <summary>The no-change singleton still checks fee, collateral bounds and its return's minimum.</summary>
    [Theory]
    [InlineData(100UL, null, false, 0UL, 100UL, 0, 0UL, 0UL, 0UL)]
    [InlineData(99UL, null, false, 0UL, 100UL, 1, 100UL, 1UL, 0UL)]
    [InlineData(100UL, 150UL, false, 0UL, 100UL, 0, 0UL, 0UL, 0UL)]
    [InlineData(99UL, 150UL, false, 0UL, 100UL, 1, 100UL, 1UL, 0UL)]
    [InlineData(100UL, 149UL, true, 0UL, 100UL, 2, 100UL, 0UL, 1UL)]
    [InlineData(200_000UL, 300_001UL, true, 4_310UL, 100UL, 2, 200_000UL, 0UL, 874_929UL)]
    public void NoChangeSingletonChecksItsWholeShape(ulong budget, ulong? collateral, bool hasReturn, ulong c,
        ulong b, int status, ulong hint, ulong spending, ulong collateralRequest)
    {
        FeeDraft draft = Synthetic(new FeeInputs(Budget: budget, CollateralBalance: collateral, ReturnPresent: hasReturn), b: b, c: c);
        FeeResult result = FeeSolver.Solve(draft);
        if (status == 0)
        {
            Assert.Equal(FeeStatus.Balanced, result.Status);
            Assert.Equal(budget, result.Fee);
            Assert.Equal(collateral ?? 0, result.TotalCollateral);
            Assert.Equal(0UL, result.Change);
        }
        else
        {
            AssertFailure(result, (FeeStatus)status);
        }
        Assert.Equal(hint, result.FeeHint);
        Assert.Equal(spending, result.SpendingRequest);
        Assert.Equal(collateralRequest, result.CollateralRequest);
    }

    /// <summary>Free fee diagnostics may request both inputs, clamp collateral, or ask from a zero budget.</summary>
    [Fact]
    public void FreeFeeFailureRequestsAreWideAndPositive()
    {
        FeeDraft both = Synthetic(new FeeInputs(ChangeIndex: 0, Budget: 1, CollateralBalance: 1, ReturnPresent: true), c: 4_310);
        FeeResult shortBoth = FeeSolver.Solve(both);
        AssertFailure(shortBoth, FeeStatus.SpendingShort | FeeStatus.CollateralShort);
        Assert.Equal(100UL, shortBoth.FeeHint);
        Assert.Equal(875_029UL, shortBoth.SpendingRequest);
        Assert.Equal(875_079UL, shortBoth.CollateralRequest);

        FeeResult wide = FeeSolver.Solve(both with { Inputs = both.Inputs with { Budget = 1_000_000 }, CollateralPercentage = ulong.MaxValue, ConstantFee = 200 });
        AssertFailure(wide, FeeStatus.CollateralShort);
        Assert.Equal(200UL, wide.FeeHint);
        Assert.Equal(ulong.MaxValue, wide.CollateralRequest);

        FeeResult zero = FeeSolver.Solve(Synthetic(new FeeInputs(ChangeIndex: 0, Budget: 0)));
        AssertFailure(zero, FeeStatus.SpendingShort);
        Assert.Equal(100UL, zero.FeeHint);
        Assert.Equal(100UL, zero.SpendingRequest);
    }

    /// <summary>A narrower output in a later region can become valid even when its coin decreases.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionWalkRechecksNarrowerOutput(bool collateral)
    {
        const ulong edge = (ulong)uint.MaxValue + 1;
        FeeInputs inputs = collateral ? new(CollateralBalance: edge + 150, ReturnPresent: true) : new(ChangeIndex: 0, Budget: edge + 100);
        FeeDraft draft = Synthetic(inputs, c: 21_582_750);
        FeeResult result = FeeSolver.Solve(draft);
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(101UL, result.Fee);
        Assert.Equal(collateral ? edge - 2 : edge - 1, collateral ? result.CollateralReturn : result.Change);
    }

    /// <summary>Empty wide regions are skipped, and inclusive walks safely reach ulong.MaxValue.</summary>
    [Fact]
    public void RegionWalkHandlesUnrepresentableRegionsAndMaximumFee()
    {
        FeeDraft draft = Synthetic(new FeeInputs(ChangeIndex: 0, Budget: ulong.MaxValue), a: 1) with
        {
            FixedBodyLength = 190,
            LedgerOverhead = 10,
            ConstantFee = ulong.MaxValue - 211
        };
        FeeResult result = FeeSolver.Solve(draft);
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(ulong.MaxValue - 1, result.Fee);
        Assert.Equal(1UL, result.Change);

        FeeDraft max = Synthetic(new FeeInputs(CollateralBalance: ulong.MaxValue), b: ulong.MaxValue, pct: 1);
        FeeResult atMax = FeeSolver.Solve(max);
        Assert.Equal(FeeStatus.Balanced, atMax.Status);
        Assert.Equal(ulong.MaxValue, atMax.Fee);
        Assert.Equal(ulong.MaxValue, atMax.TotalCollateral);
        Assert.Throws<OverflowException>(() => FeeSolver.Solve(max with { FeePerByte = 1 }));
    }

    /// <summary>The return-only walk clamps its wide upper bound and can price a fee above the truncated bound.</summary>
    [Fact]
    public void RegionWalkClampsWideCollateralBound()
    {
        FeeResult result = FeeSolver.Solve(Synthetic(new FeeInputs(CollateralBalance: ulong.MaxValue, ReturnPresent: true),
            b: ulong.MaxValue - 10, pct: 1));
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(ulong.MaxValue - 10, result.Fee);
    }

    /// <summary>The walk includes zero and accepts fees one below either upper bound.</summary>
    [Theory]
    [InlineData(false, 1UL, 0UL, 1UL)]
    [InlineData(false, 300UL, 299UL, 1UL)]
    [InlineData(true, 154UL, 101UL, 2UL)]
    public void RegionWalkCoversBoundEdges(bool collateral, ulong balance, ulong fee, ulong remainder)
    {
        FeeInputs inputs = collateral ? new(CollateralBalance: balance, ReturnPresent: true) : new(ChangeIndex: 0, Budget: balance);
        FeeResult result = FeeSolver.Solve(Synthetic(inputs, b: fee));
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(fee, result.Fee);
        Assert.Equal(remainder, collateral ? result.CollateralReturn : result.Change);
    }

    /// <summary>A fee outside its computed region cannot win over a smaller fee in the next region.</summary>
    [Fact]
    public void RegionWalkFindsLeastFeeAcrossRegions()
    {
        FeeDraft seed = Synthetic(new FeeInputs(ChangeIndex: 0, Budget: uint.MaxValue + 1_000UL), a: 1, b: 0);
        FeeResult result = FeeSolver.Solve(seed with { ConstantFee = 1_000 - seed.FixedBodyLength - seed.LedgerOverhead });
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(1_008UL, result.Fee);
    }

    /// <summary>With a nonzero byte fee a failure hint prices change, total and return at nine bytes each.</summary>
    [Fact]
    public void FailureHintPricesWidestOutputs()
    {
        FeeDraft draft = Synthetic(new FeeInputs(ChangeIndex: 0, Budget: 1, CollateralBalance: 1, ReturnPresent: true), a: 1);
        FeeResult result = FeeSolver.Solve(draft);
        AssertFailure(result, FeeStatus.SpendingShort | FeeStatus.CollateralShort);
        ulong k = draft.FixedBodyLength + draft.LedgerOverhead + 27 + 100;
        Assert.Equal(k + 2 <= 255 ? k + 2 : k + 3, result.FeeHint);
    }

    /// <summary>Preparation removes the written total-collateral width, not its canonical width.</summary>
    [Fact]
    public void PrepareUsesWrittenTotalCollateralWidth()
    {
        JsonElement row = Row("F09");
        PostMaryTransaction seed = Tx(row);
        byte[] prefix = [0xa5, 0, 0x80, 1, 0x80, 2, 0, 16, .. Return(seed)!.Raw.Span, 17];
        PostMaryTransaction WithBody(byte[] bytes) => PostMaryTransaction.Create(ConwayTransactionBody.Read(bytes), seed.Witnesses, true, null);
        ProtocolParams pp = Pp(row) with { AdaPerUTxOByte = 0, MinFeeA = 1 };
        FeeInputs inputs = new(CollateralBalance: 10_000_000, ReturnPresent: true);
        FeeDraft canonical = FeeSolver.Prepare(WithBody([.. prefix, 0]), pp, inputs);
        FeeDraft wider = FeeSolver.Prepare(WithBody([.. prefix, 0x18, 0]), pp, inputs);
        FeeResult result = FeeSolver.Solve(wider);
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(FeeSolver.Solve(canonical), result);
    }

    /// <summary>Builder inputs are honored as a floor on the fee and an addition to the fee.</summary>
    [Theory]
    [InlineData(500_000UL, 0UL, 500_000UL)]
    [InlineData(0UL, 1_000UL, 1_100UL)]
    public void RegionWalkHonorsMinimumFeeAndPadding(ulong minimum, ulong padding, ulong expected)
    {
        FeeResult result = FeeSolver.Solve(Synthetic(new FeeInputs(ChangeIndex: 0, Budget: 10_000_000,
            MinimumFee: minimum, Padding: padding)));
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.Equal(expected, result.Fee);
    }

    /// <summary>Script units are summed wide and rationals never multiply all four operands together.</summary>
    [Fact]
    public void ExactArithmeticHandlesWideAndInvalidPrices()
    {
        ProtocolParams pp = Pp(Row("F13"));
        CborRationalNumber almostOne = new(ulong.MaxValue - 1, ulong.MaxValue);
        Assert.Equal(3UL, FeeEngine.ScriptFee(Units((2, 1)), pp with { ExecutionCosts = ExUnitPrices.Create(almostOne, almostOne) }));
        CborRationalNumber half = new(1, 2);
        Assert.Equal(1UL, FeeEngine.ScriptFee(Units((1, 1)), pp with { ExecutionCosts = ExUnitPrices.Create(half, half) }));
        Assert.Equal(ulong.MaxValue, FeeEngine.ScriptFee(Units((ulong.MaxValue, 0), (ulong.MaxValue, 0)),
            pp with { ExecutionCosts = ExUnitPrices.Create(half, half) }));
        CborRationalNumber zero = new(0, 1);
        Assert.Equal(0UL, FeeEngine.ScriptFee(Units((ulong.MaxValue, ulong.MaxValue)), pp with { ExecutionCosts = ExUnitPrices.Create(zero, zero) }));
        Assert.Throws<InvalidDataException>(() => FeeEngine.ScriptFee(Units((0, 0)),
            pp with { ExecutionCosts = ExUnitPrices.Create(new CborRationalNumber(1, 0), zero) }));
        CborRationalNumber huge = new(ulong.MaxValue, 1);
        Assert.Throws<OverflowException>(() => FeeEngine.ScriptFee(Units((ulong.MaxValue, 0), (ulong.MaxValue, 0)),
            pp with { ExecutionCosts = ExUnitPrices.Create(huge, zero) }));
        Assert.Equal(ulong.MaxValue, FeeEngine.RequiredCollateral(ulong.MaxValue, 100));
        Assert.Throws<OverflowException>(() => FeeEngine.LinearFee(ulong.MaxValue, pp));
    }

    /// <summary>Replacing the fee field subtracts its actual wire width, and witnesses use Sign's transform.</summary>
    [Fact]
    public void MinFeeUsesWireWidthAndSigningTransform()
    {
        JsonElement row = Row("F01");
        PostMaryTransaction tx = Tx(row);
        ProtocolParams pp = Pp(row);
        Assert.Equal(Number(row, "q"), FeeEngine.MinFee(tx.Sign([Placeholder()]), pp));
        Assert.Throws<ArgumentOutOfRangeException>(() => FeeEngine.MinFee(tx, pp, -1));
        Assert.Throws<ArgumentException>(() => FeeEngine.MinFee(default(ShelleyTransaction), pp));

        byte[] nonCanonical = [0xa3, 0, 0x80, 1, 0x80, 2, 0x18, 1];
        byte[] canonical = [0xa3, 0, 0x80, 1, 0x80, 2, 1];
        PostMaryTransaction WithBody(byte[] bytes) => PostMaryTransaction.Create(ConwayTransactionBody.Read(bytes), tx.Witnesses, true, null);
        Assert.Equal(FeeEngine.MinFee(WithBody(canonical), pp), FeeEngine.MinFee(WithBody(nonCanonical), pp));
    }

    /// <summary>An explicitly decoded Alonzo list redeemer is still charged when adding a VKey witness.</summary>
    [Fact]
    public void MinFeePreservesAlonzoRedeemerWithAddedWitness()
    {
        ITransaction tx = ITransaction.Read(Convert.FromHexString("84a3008001800200a1058184000000821903e800f5f6"), Era.Alonzo);
        ProtocolParams pp = Pp(Row("F13")) with { MinFeeA = 1, MinFeeB = 0, ExecutionCosts = ExUnitPrices.Create(new(1, 1), new(1, 1)) };
        // 1,000 script + list(1), repriced body(9), existing witness map(12), null(1), new key(1), tag(3), list(1), witness(101).
        Assert.Equal(1_129UL, FeeEngine.MinFee(tx, pp, 1));
    }

    /// <summary>Extra-key quotes use the map and list framing actually emitted by the signer.</summary>
    [Theory]
    [InlineData("BFFF", 165_633UL)]
    [InlineData("A100F6", 165_633UL)]
    [InlineData("A102F7", 165_633UL)]
    [InlineData("BF00D9010281", 170_077UL)]
    [InlineData("B8011800D9010281", 170_077UL)]
    [InlineData("BF1800D901029F", 170_077UL)]
    public void MinFeeMatchesSignerOnReframedWitnessMap(string prefix, ulong expected)
    {
        JsonElement row = Row("F01");
        PostMaryTransaction seed = Tx(row);
        VKeyWitness existing = Placeholder();
        byte[] map = prefix switch
        {
            "BFFF" or "A100F6" or "A102F7" => Convert.FromHexString(prefix),
            "BF00D9010281" => [.. Convert.FromHexString(prefix), .. existing.Raw.Span, 0xff],
            "B8011800D9010281" => [.. Convert.FromHexString(prefix), .. existing.Raw.Span],
            _ => [.. Convert.FromHexString(prefix), .. existing.Raw.Span, 0xff, 0xff]
        };
        byte[] envelope = [0x84, .. seed.Body.Raw.Span, .. map, 0xf5, 0xf6];
        ITransaction tx = ITransaction.Read(envelope, Era.Conway);
        byte[] key = new byte[32];
        key[0] = 1;
        VKeyWitness added = VKeyWitness.Create(key, new byte[64]);
        ProtocolParams pp = Pp(row);
        Assert.Equal(expected, FeeEngine.MinFee(tx.Sign([added]), pp));
        Assert.Equal(expected, FeeEngine.MinFee(tx, pp, 1));
    }

    /// <summary>The signer re-encodes known map keys and omits unknown fields of a concrete witness-set decode.</summary>
    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)8)]
    public void MinFeeMatchesSignerOnReencodedFieldKey(byte key)
    {
        JsonElement row = Row("F01b");
        PostMaryTransaction seed = Tx(row);
        PostAlonzoTransactionWitnessSet witnesses = (PostAlonzoTransactionWitnessSet)seed.Witnesses;
        byte[] map = [0xa1, 0x18, key, .. witnesses.BootstrapWitnesses!.Value.Raw.Span];
        PostMaryTransaction tx = PostMaryTransaction.Create(seed.Body, PostAlonzoTransactionWitnessSet.Read(map), seed.IsValid, seed.AuxiliaryData);
        ProtocolParams pp = Pp(row);
        PostMaryTransaction signed = (PostMaryTransaction)tx.Sign([Placeholder()]);
        if (key == 8)
        {
            Assert.True(signed.Witnesses.Raw.Length < tx.Witnesses.Raw.Length);
        }
        Assert.Equal(FeeEngine.MinFee(signed, pp), FeeEngine.MinFee(tx, pp, 1));
    }

    /// <summary>The scalar quote matches Sign at and around a list-header boundary for every verified row.</summary>
    [Theory]
    [MemberData(nameof(Rows), "balanced")]
    [MemberData(nameof(Rows), "quote")]
    [MemberData(nameof(Rows), "candidate")]
    [MemberData(nameof(Rows), "min-utxo")]
    public void MinFeeMatchesSignerAcrossWitnessCounts(string id, int index)
    {
        JsonElement row = Row(id, index);
        PostMaryTransaction tx = Tx(row);
        ProtocolParams pp = Pp(row);
        ulong referenceSize = row.TryGetProperty("s", out JsonElement size) ? size.GetUInt64() : 0;
        foreach (int count in new[] { 1, 2, 23, 24, 30 })
        {
            List<VKeyWitness> added = [];
            for (int i = 0; i < count; i++)
            {
                byte[] key = new byte[32];
                key[0] = (byte)i;
                added.Add(VKeyWitness.Create(key, new byte[64]));
            }
            ulong expected = FeeEngine.MinFee(tx.Sign(added), pp, referenceScriptSize: referenceSize);
            ulong actual = FeeEngine.MinFee(tx, pp, count, referenceSize);
            Assert.True(expected == actual, $"{id}[{index}] with {count} added keys: expected {expected}, got {actual}.");
        }
    }

    /// <summary>Large witness counts change encoded lengths, not the amount allocated by a quote.</summary>
    [Fact]
    public void MinFeeDoesNotMaterializeAdditionalWitnesses()
    {
        JsonElement row = Row("F01");
        PostMaryTransaction tx = Tx(row);
        ProtocolParams pp = Pp(row) with { MinFeeA = 1, MinFeeB = 1_000_000 };
        const int count = 65_536;
        ulong one = FeeEngine.MinFee(tx, pp, 1);
        ulong many = 0;
        void Quote() => many = FeeEngine.MinFee(tx, pp, count);
        long allocated = AllocationAssert.Measure(Quote, Quote);
        Assert.Equal(one + (101 * (count - 1)) + 4, many);
        Assert.InRange(allocated, 0L, 1_000_000L);
    }

    /// <summary>Public entry points reject a null reference at the boundary, naming the parameter.</summary>
    [Fact]
    public void RejectsNullFeeInputs()
    {
        ProtocolParams pp = Pp(Row("F01"));
        Assert.Throws<ArgumentNullException>("tx", () => FeeEngine.MinFee(null!, pp));
        Assert.Throws<ArgumentNullException>("output", () => FeeEngine.MinLovelace(null!, 1));
        Assert.Throws<ArgumentNullException>("output", () => FeeEngine.LeastValidCoin(null!, 1));
        Assert.Throws<ArgumentNullException>("inputs", () => FeeEngine.ReferenceScriptSize(null!, [], []));
        Assert.Throws<ArgumentNullException>("referenceInputs", () => FeeEngine.ReferenceScriptSize([], null!, []));
    }

    /// <summary>Reference fees cap the byte input and require each outref, even if it carries no script.</summary>
    [Fact]
    public void ReferenceFeeRejectsOversizeAndUnresolvedInputs()
    {
        JsonElement row = Row("F11");
        ProtocolParams pp = Pp(row);
        Assert.Throws<ArgumentOutOfRangeException>(() => FeeEngine.ReferenceScriptFee(204_801, pp));
        Assert.Throws<InvalidDataException>(() => FeeEngine.ReferenceScriptFee(1, pp with { MinFeeRefScriptCostPerByte = new(1, 0) }));
        TransactionInput missing = Assert.Single(Tx(row).Body.Inputs());
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => FeeEngine.ReferenceScriptSize([missing], [], []));
        Assert.Contains($"{Convert.ToHexStringLower(missing.TransactionId.Span)}#{missing.Index}", error.Message, StringComparison.Ordinal);
    }

    private static JsonElement Row(string id, int index = 0) => Vectors.Cases.Single(item => Text(item, "id") == id).GetProperty("rows")[index];
    private static PostMaryTransaction Tx(JsonElement row) => (PostMaryTransaction)ITransaction.Read(Vectors.Bytes(Text(row, "tx")), Era.Conway);
    private static ProtocolParams Pp(JsonElement row) => Vectors.Parameters(Text(row, "parameters"));
    private static ITransactionOutput Output(JsonElement row) => Tx(row).Body.Outputs().ElementAt(checked((int)Number(row, "output")));
    private static ITransactionOutput? Return(PostMaryTransaction tx) => ((ConwayTransactionBody)tx.Body).CollateralReturn;
    private static IRedeemers? Redeemers(PostMaryTransaction tx) => ((PostAlonzoTransactionWitnessSet)tx.Witnesses).Redeemers;
    private static VKeyWitness Placeholder() => VKeyWitness.Create(new byte[32], new byte[64]);
    private static int OneWitnessDelta(PostMaryTransaction tx) => ((PostMaryTransaction)tx.Sign([Placeholder()])).Witnesses.Raw.Length - tx.Witnesses.Raw.Length;
    private static ulong LedgerSize(PostMaryTransaction tx) => (ulong)tx.Body.Raw.Length + (ulong)tx.Witnesses.Raw.Length + (ulong)(tx.AuxiliaryData?.Raw.Length ?? 1) + 1;
    private static ulong Quote(PostMaryTransaction tx, JsonElement row) => FeeEngine.MinFee(tx, Pp(row), checked((int)Number(row, "w")), Number(row, "s"));
    private static RedeemerList Units(params (ulong Mem, ulong Steps)[] units) => RedeemerList.Create([.. units.Select((u, i) =>
        RedeemerEntry.Create(0, (ulong)i, PlutusInt.Create(0), ExUnits.Create(u.Mem, u.Steps)))]);

    private static ITransactionOutput WithCoin(ITransactionOutput output, ulong coin)
    {
        IValue amount = output.Amount() is LovelaceWithMultiAsset assets
            ? LovelaceWithMultiAsset.Create(coin, assets.MultiAsset) : Lovelace.FromAmount(coin);
        return output switch
        {
            PostAlonzoTransactionOutput map => PostAlonzoTransactionOutput.Create(map.Address, amount, map.Datum, map.ScriptRef),
            AlonzoTransactionOutput list => AlonzoTransactionOutput.Create(list.Address, amount, list.DatumHash),
            _ => throw new InvalidDataException("Unexpected output form")
        };
    }

    private static FeeDraft VectorDraft(JsonElement row)
    {
        PostMaryTransaction tx = Tx(row);
        int index = tx.Body.Outputs().Count() - 1;
        Dictionary<string, ulong> coins = Vectors.Lovelace(Text(row, "utxo"));
        ulong Sum(IEnumerable<TransactionInput> inputs) => inputs.Aggregate(0UL,
            (sum, input) => checked(sum + coins[$"{Convert.ToHexStringLower(input.TransactionId.Span)}#{input.Index}"]));
        ulong budget = checked(Sum(tx.Body.Inputs()) - tx.Body.Outputs().Take(index).Aggregate(0UL, (sum, output) => checked(sum + output.Amount().Lovelace())));
        ulong? collateral = tx.Body.Collateral() is { } inputs ? Sum(inputs) : null;
        return FeeSolver.Prepare(tx, Pp(row), new FeeInputs(index, budget, CollateralBalance: collateral,
            ReturnPresent: Return(tx) is not null, ReferenceScriptSize: Number(row, "s"), SigningDelta: (ulong)OneWitnessDelta(tx)));
    }

    private static FeeDraft Synthetic(FeeInputs inputs, ulong a = 0, ulong b = 100, ulong c = 0, ulong pct = 150)
    {
        JsonElement row = Row("F01");
        PostMaryTransaction seed = Tx(row);
        Address address = new(seed.Body.Outputs().First().Address());
        PostAlonzoTransactionOutput output = PostAlonzoTransactionOutput.Create(address, Lovelace.FromAmount(1));
        ConwayTransactionBody body = ConwayTransactionBody.Create(
            inputs: ((ConwayTransactionBody)seed.Body).Inputs, outputs: CborList.Create<ITransactionOutput>([output]), fee: 0,
            collateralReturn: inputs.ReturnPresent ? output : null,
            totalCollateral: inputs.CollateralBalance is { } balance ? inputs.ReturnPresent ? 0 : balance : null);
        PostMaryTransaction tx = PostMaryTransaction.Create(body, seed.Witnesses, true, null);
        ProtocolParams pp = Pp(row) with { MinFeeA = a, MinFeeB = b, AdaPerUTxOByte = c, CollateralPercentage = pct };
        return FeeSolver.Prepare(tx, pp, inputs);
    }

    private static void AssertFailure(FeeResult result, FeeStatus status)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal((0UL, 0UL, 0UL, 0UL, 0UL, 0UL, 0UL), (result.Fee, result.Change,
            result.TotalCollateral, result.CollateralReturn, result.BodyLength, result.LedgerSize, result.SigningDelta));
    }
}

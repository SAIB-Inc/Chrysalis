using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Byron;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Test;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Codec.Types.Plutus;
using Chrysalis.Plutus.VM.Models;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Fees;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Test.Cli;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using Xunit;
using static Chrysalis.Tx.Test.Cli.CliVectors;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Test.Fees;

/// <summary>Prices actual signed builder bytes, independently of the builder's draft.</summary>
public sealed class BuilderFeeTests(BuilderFeeFixture fixture) : IClassFixture<BuilderFeeFixture>
{
    [Fact]
    public async Task TransferUsesTheSignedQuoteExactlyOnce()
    {
        JsonElement row = fixture.Row("F01");
        ResolvedInput input = Assert.Single(fixture.Inputs(row));
        ITransactionOutput payment = fixture.Tx(row).Body.Outputs().First();
        ProtocolParams pp = fixture.Pp(row);
        using BuildCount builds = new();
        PostMaryTransaction result = await Transfer(pp, input, payment.Amount().Lovelace()).Complete();
        Assert.Equal(2, builds.Value);
        ulong quote = FeeEngine.MinFee(Sign(result), pp);
        ulong unsignedQuote = FeeEngine.MinFee(result, pp);
        Assert.True(quote > unsignedQuote);
        Assert.Equal(quote, result.Body.Fee());
        Assert.NotEqual(quote + (quote - unsignedQuote), result.Body.Fee());
        AssertConservation(result, input.Output.Amount().Lovelace());
    }

    [Fact]
    public async Task V3SpendPricesEvaluatedUnitsAndCollateral()
    {
        (ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, ExUnits units) = ScriptVector();
        PostMaryTransaction tx = await ScriptSpend(pp, input, collateral, script, (_, _) => Result(units.Mem, units.Steps)).Complete();
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
        ulong total = FeeEngine.RequiredCollateral(tx.Body.Fee(), pp.CollateralPercentage!.Value);
        Assert.Equal(total, tx.Body.TotalCollateral());
        Assert.Equal(collateral.Output.Amount().Lovelace() - total, tx.Body.CollateralChange()!.Amount().Lovelace());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReferenceFeeCountsPlainInputsAndReadOnlyReferences(bool readOnly)
    {
        JsonElement row = fixture.Row("F11");
        PostMaryTransaction seed = fixture.Tx(row);
        ResolvedInput carrier = Assert.Single(fixture.Inputs(row));
        ProtocolParams pp = fixture.Pp(row);
        TxBuilder builder = readOnly ? Transfer(pp, Utxo(1, 30_000_000), 2_000_000).AddReferenceInput(carrier)
            : Transfer(pp, carrier, 2_000_000);
        PostMaryTransaction tx = Sign(await builder.Complete());
        Assert.Equal(FeeEngine.MinFee(tx, pp, referenceScriptSize: 25_700), tx.Body.Fee());
        ulong seedLinear = FeeEngine.MinFee(seed, pp, extraVKeyWitnesses: checked((int)Number(row, "w")));
        Assert.Equal(Number(row, "q") - seedLinear, tx.Body.Fee() - FeeEngine.MinFee(tx, pp));
    }

    [Fact]
    public async Task ChangeWidthGapUsesLeastValidFee()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        const ulong bound = uint.MaxValue;
        const ulong payment = 2_000_000;
        // Price the shape at the narrower width before setting the input budget.
        TransactionBuilder shape = Low(pp, Utxo(0x11, 0), payment).AddOutput(Recipient, Lovelace.FromAmount(bound), isChange: true);
        ulong narrow = FeeEngine.MinFee(Sign(shape.Build()), pp);
        ResolvedInput input = Utxo(0x11, payment + bound + narrow);
        PostMaryTransaction tx = await Transfer(pp, input, payment).Complete();
        Assert.Equal(bound, tx.Body.Outputs().Last().Amount().Lovelace());
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
        AssertConservation(tx, input.Output.Amount().Lovelace());
    }

    [Fact]
    public void SolveOnPreparedBuilderDraftIsAllocationFree()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        const ulong payment = 2_000_000, budget = uint.MaxValue + 100_000UL;
        ResolvedInput input = Utxo(1, payment + budget);
        TransactionBuilder builder = Low(pp, input, payment).AddOutput(Recipient, Lovelace.FromAmount(budget), isChange: true);
        FeeDraft draft = TransactionBuilderExtensions.PrepareStructure(builder, [input], new FeeInputs(Budget: budget), 0);
        FeeResult result = default;
        void Solve() => result = FeeSolver.Solve(draft);
        Assert.Equal(0, AllocationAssert.Measure(Solve, Solve));
        Assert.Equal(FeeStatus.Balanced, result.Status);
        Assert.True(result.Change <= uint.MaxValue);
    }

    [Fact]
    public async Task AFailedStructureAddsOneDraftAndNoResultBuild()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01")) with { MinFeeA = 0, MinFeeB = 200_000 };
        const ulong payment = 2_000_000;
        ResolvedInput first = Utxo(1, payment + 200_000);
        ResolvedInput next = Utxo(2, 1_000_000);
        using BuildCount builds = new();
        PostMaryTransaction tx = await Transfer(pp, first, payment).AddUnspentOutputs([next]).Complete();
        Assert.Equal(3, builds.Value);
        Assert.Equal(2, tx.Body.Inputs().Count());
        Assert.Null(Assert.IsType<PostAlonzoTransactionWitnessSet>(tx.Witnesses).VKeyWitnesses);
    }

    [Fact]
    public void InputComparerSortsByIdBytesThenIndexWithoutAllocating()
    {
        TransactionInput low9 = TransactionInput.Create((byte[])[0x0a, .. new byte[31]], 9);
        TransactionInput low10 = TransactionInput.Create(low9.TransactionId, 10);
        TransactionInput high = TransactionInput.Create((byte[])[0xa0, .. new byte[31]], 0);
        TransactionInput[] unsorted = [high, low10, low9];
        Assert.Equal([low9, low10, high], unsorted.Order(TxBuilder.InputComparer));
        int order = 0;
        void Compare() => order = TxBuilder.InputComparer.Compare(low9, low10);
        Assert.Equal(0, AllocationAssert.Measure(Compare, Compare));
        Assert.True(order < 0);
    }

    [Fact]
    public void LowLevelMultiAssetPricesItsOwnSignedBytes()
    {
        JsonElement row = fixture.Row("F03");
        PostMaryTransaction seed = fixture.Tx(row);
        ProtocolParams pp = fixture.Pp(row);
        ITransactionOutput[] outputs = [.. seed.Body.Outputs()];
        TransactionBuilder builder = TransactionBuilder.Create(pp).AddInput(seed.Body.Inputs().Single()).AddOutput(outputs[0])
            .AddOutput(Recipient, outputs[1].Amount(), isChange: true);
        _ = builder.CalculateFee([]);
        PostMaryTransaction tx = builder.Build();
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
        Assert.Equal(((LovelaceWithMultiAsset)outputs[1].Amount()).MultiAsset.Raw,
            ((LovelaceWithMultiAsset)tx.Body.Outputs().Last().Amount()).MultiAsset.Raw);
    }

    [Fact]
    public void AttachedScriptIsNotAReferenceFee()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F09"));
        PlutusV3Script script = PlutusV3Script.Create(24, new byte[1000]);
        TransactionBuilder builder = Low(pp, Utxo(1, 5_000_000), 2_000_000)
            .AddPlutusScript(script).SetRedeemers(RedeemerMap.Create(new Dictionary<RedeemerKey, RedeemerValue>
            {
                [RedeemerKey.Create(0, 0)] = RedeemerValue.Create(PlutusInt.Create(0), ExUnits.Create(0, 0))
            }));
        _ = builder.CalculateFee([script]);
        PostMaryTransaction tx = builder.Build();
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
    }

    [Fact]
    public void KnownInputsPriceReferenceScriptsAndRejectUnresolvedReferences()
    {
        JsonElement row = fixture.Row("F11");
        ProtocolParams pp = fixture.Pp(row);
        ResolvedInput reference = fixture.Inputs(row).Single(u => u.Output.ScriptRef() is not null);
        TransactionBuilder b = Low(pp, reference, 2_000_000);
        _ = b.CalculateFee([], resolvedInputs: [reference]);
        Assert.Equal(FeeEngine.MinFee(Sign(b.Build()), pp, referenceScriptSize: 25_700), b.Fee);
        TransactionInput missing = Utxo(0xb6, 1).Outref;
        _ = b.AddReferenceInput(missing).AddRequiredSigner(new byte[28]);
        ConwayTransactionBody body = b.BuildBody();
        Assert.True(SameOutref(missing, Assert.Single(body.ReferenceInputs!.Value)));
        Assert.Equal(new byte[28], Assert.Single(body.RequiredSigners!.Value).ToArray());
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => b.CalculateFee([], resolvedInputs: [reference]));
        Assert.Contains($"{Convert.ToHexStringLower(missing.TransactionId.Span)}#{missing.Index}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LowLevelReadOnlyReferencesAreResolvedButNeverFundChange()
    {
        JsonElement row = fixture.Row("F11");
        ProtocolParams pp = fixture.Pp(row);
        ResolvedInput reference = Assert.Single(fixture.Inputs(row));
        ResolvedInput spend = Utxo(0xb6, 5_000_000);
        TransactionBuilder builder = Low(pp, spend, 2_000_000).AddReferenceInput(reference.Outref);
        _ = builder.CalculateFee([], changeAddress: Recipient, resolvedInputs: [spend], referenceInputs: [reference]);
        PostMaryTransaction tx = builder.Build();
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp, referenceScriptSize: 25_700), tx.Body.Fee());
        AssertConservation(tx, spend.Output.Amount().Lovelace());
    }

    [Fact]
    public void UnknownInputsUseTheStatedVKeyCount()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        TransactionBuilder builder = Low(pp, Utxo(1, 5_000_000), 2_000_000).CalculateFee([], mockWitnessFee: 2);
        Assert.Equal(FeeEngine.MinFee(Sign(builder.Build(), 2), pp), builder.Fee);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(8UL, false)]
    [InlineData(9UL, true)]
    public void MissingReferencePriceOnlyMeansZeroBeforeConway(ulong? major, bool reject)
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F11")) with
        {
            MinFeeRefScriptCostPerByte = null,
            ProtocolVersion = major is { } version ? ProtocolVersion.Create(version, 0) : null
        };
        Assert.Equal(0UL, FeeEngine.ReferenceScriptFee(0, pp));
        if (reject)
        {
            Assert.Contains(nameof(pp.MinFeeRefScriptCostPerByte), Assert.Throws<InvalidDataException>(() => FeeEngine.ReferenceScriptFee(1, pp)).Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(0UL, FeeEngine.ReferenceScriptFee(1, pp));
        }
    }

    [Fact]
    public async Task TemplateValidSlotsDatumAndRequiredSignersSurvivePricing()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        ResolvedInput input = Utxo(1, 30_000_000);
        const ulong from = 10, until = 20;
        TransactionTemplate<int> template = TransactionTemplateBuilder.Create<int>(new CliSnapshotProvider(pp, [input, Utxo(2, 5_000_000)]))
            .AddStaticParty("sender", Recipient).AddStaticParty("recipient", OtherRecipient).SetChangeAddress(Recipient)
            .AddInput((options, _) => options.Utxo = input)
            .SetValidFrom(from).SetValidTo(until)
            .AddRequiredSigner("recipient")
            .AddOutput((options, _, _) =>
            {
                options.To = "recipient";
                options.Amount = Lovelace.FromAmount(10_000_000);
                options.SetDatum(PlutusInt.Create(42));
            }).Build();
        PostMaryTransaction tx = (PostMaryTransaction)await template(0);
        ConwayTransactionBody body = (ConwayTransactionBody)tx.Body;
        Assert.Equal(from, body.ValidityIntervalStart);
        Assert.Equal(until, body.TimeToLive);
        Assert.NotNull(Assert.IsType<PostAlonzoTransactionOutput>(body.Outputs.First()).Datum);
        Assert.Equal(FeeEngine.MinFee(Sign(tx, 2), pp), body.Fee);
    }

    [Fact]
    public void InitialWithdrawalAffectsWitnessesAndChange()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        ResolvedInput input = Utxo(1, 5_000_000);
        const ulong withdrawal = 1_000_000;
        TransactionBuilder b = Low(pp, input, 2_000_000).AddWithdrawal("e0" + new string('3', 56), withdrawal);
        _ = b.CalculateFee([], changeAddress: Recipient, resolvedInputs: [input]);
        PostMaryTransaction tx = b.Build();
        Assert.Equal(FeeEngine.MinFee(Sign(tx, 2), pp), b.Fee);
        AssertConservation(tx, input.Output.Amount().Lovelace() + withdrawal);
    }

    [Fact]
    public async Task SharedPaymentKeyAndDistinctChangeAddressRequireJustOneWitness()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        ResolvedInput a = Utxo(1, 8_000_000), b = Utxo(2, 8_000_000);
        PostMaryTransaction tx = await Transfer(pp, a, 10_000_000).AddInput(b).SetChangeAddress(OtherRecipient).Complete();
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
    }

    [Fact]
    public void SignPreservesEveryAlonzoWitnessField()
    {
        AlonzoTransactionWitnessSet old = AlonzoTransactionWitnessSet.Create(
            nativeScripts: CborList.Create<INativeScript>([ScriptPubKey.Create(0, new byte[28])]),
            bootstrapWitnesses: CborList.Create([Bootstrap()]),
            plutusV1Scripts: CborList.Create<ReadOnlyMemory<byte>>(["B"u8.ToArray()]),
            plutusDataSet: CborList.Create<IPlutusData>([PlutusInt.Create(123)]),
            redeemers: RedeemerList.Create([RedeemerEntry.Create(0, 0, PlutusInt.Create(42), ExUnits.Create(1, 2))]));
        PostMaryTransaction source = (PostMaryTransaction)ITransaction.Read(
            CborSerializer.Serialize(PostMaryTransaction.Create(fixture.Tx(fixture.Row("F01")).Body, old, true, null)),
            Era.Alonzo);
        _ = Assert.IsType<AlonzoTransactionWitnessSet>(source.Witnesses);
        PostMaryTransaction signed = Sign(source);
        PostAlonzoTransactionWitnessSet updated = Assert.IsType<PostAlonzoTransactionWitnessSet>(signed.Witnesses);
        Assert.Equal(old.NativeScripts!.Value.Raw.ToArray(), updated.NativeScripts!.Value.Raw.ToArray());
        Assert.Equal(old.BootstrapWitnesses!.Value.Raw.ToArray(), updated.BootstrapWitnesses!.Value.Raw.ToArray());
        Assert.Equal(old.PlutusV1Scripts!.Value.Raw.ToArray(), updated.PlutusV1Scripts!.Value.Raw.ToArray());
        Assert.Equal(old.PlutusDataSet!.Value.Raw.ToArray(), updated.PlutusDataSet!.Value.Raw.ToArray());
        Assert.Equal(old.Redeemers!.Raw.ToArray(), updated.Redeemers!.Raw.ToArray());
        Assert.Single(updated.VKeyWitnesses!.Value);
    }

    [Fact]
    public async Task ReevaluatesTheActualFinalFeeUntilUnitsSettle()
    {
        (ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, ExUnits units) = ScriptVector();
        int calls = 0;
        using BuildCount builds = new();
        TxBuilder builder = ScriptSpend(pp, input, collateral, script, async (_, _) =>
        {
            await Task.Yield();
            return [new(0, 0, new(++calls < 2 ? units.Mem : units.Mem + 100_000, units.Steps))];
        });
        PostMaryTransaction tx = await builder.Complete();
        Assert.Equal(3, calls);
        Assert.Equal(1 + (2 * (calls - 1)), builds.Value);
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
        PostAlonzoTransactionWitnessSet witnesses = Assert.IsType<PostAlonzoTransactionWitnessSet>(tx.Witnesses);
        RedeemerMap redeemers = Assert.IsType<RedeemerMap>(witnesses.Redeemers);
        RedeemerValue entry = Assert.Single(redeemers.Value).Value;
        Assert.Equal(units.Mem + 100_000, entry.ExUnits.Mem);
        Assert.Equal(LedgerScriptDataHash(tx, 2, pp.CostModelsForScriptLanguage?.PlutusV3), tx.Body.ScriptDataHash()?.ToArray());
    }

    // The node sends cost models as indefinite-length lists (the G1 wire reply); cardano-cli's F09 hash is the V3 reference, the ledger rule the V2 one.
    [Fact]
    public async Task ScriptDataHashUsesTheLedgersFixedLengthCostList()
    {
        (ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, ExUnits units) = ScriptVector();
        Assert.True(pp.CostModelsForScriptLanguage?.PlutusV3?.IsIndefinite);
        PostMaryTransaction seed = fixture.Tx(fixture.Row("F09"));
        PostMaryTransaction tx = await ScriptSpend(pp, input, collateral, script, (_, _) => Result(units.Mem, units.Steps)).Complete();
        Assert.Equal(((PostAlonzoTransactionWitnessSet)seed.Witnesses).Redeemers?.Raw.ToArray(), ((PostAlonzoTransactionWitnessSet)tx.Witnesses).Redeemers?.Raw.ToArray());
        Assert.Equal(seed.Body.ScriptDataHash()?.ToArray(), LedgerScriptDataHash(seed, 2, pp.CostModelsForScriptLanguage?.PlutusV3));
        Assert.Equal(seed.Body.ScriptDataHash()?.ToArray(), tx.Body.ScriptDataHash()?.ToArray());
        Assert.True(pp.CostModelsForScriptLanguage?.PlutusV2?.IsIndefinite);
        PlutusV2Script v2 = PlutusV2Script.Create(2, script.ScriptBytes);
        ResolvedInput locked = new(input.Outref, PostAlonzoTransactionOutput.Create(new Address((byte[])[input.Output.Address().Span[0], .. v2.Hash()]),
            input.Output.Amount(), DatumOptionExtensions.InlineDatumFrom(PlutusInt.Create(42))));
        PostMaryTransaction v2Tx = await ScriptSpend(pp, locked, collateral, v2, (_, _) => Result(units.Mem, units.Steps)).Complete();
        Assert.Equal(LedgerScriptDataHash(v2Tx, 1, pp.CostModelsForScriptLanguage?.PlutusV2), v2Tx.Body.ScriptDataHash()?.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrOscillatingEvaluationThrows(bool oscillates)
    {
        (ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, ExUnits units) = ScriptVector();
        int calls = 0;
        TxBuilder builder = ScriptSpend(pp, input, collateral, script, (_, _) => oscillates
            ? Result(units.Mem + ((ulong)(++calls % 2) * 100_000), units.Steps)
            : Task.FromResult<IReadOnlyList<EvaluationResult>>([]));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(builder.Complete);
        Assert.Contains(oscillates ? "script units did not settle" : "(0, 0)", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateOrUnknownEvaluationPointerThrows(bool duplicate)
    {
        (ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, ExUnits units) = ScriptVector();
        TxBuilder builder = ScriptSpend(pp, input, collateral, script, (_, _) => Task.FromResult<IReadOnlyList<EvaluationResult>>(
            [new(0, 0, new(units.Mem, units.Steps)), new(0, duplicate ? 0UL : 1UL, new(units.Mem, units.Steps))]));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(builder.Complete);
        Assert.Contains(duplicate ? "(0, 0)" : "(0, 1)", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(10_000_000UL)]
    public async Task OutputsKeepRequestedCoinOrUseTheirOwnMinimum(ulong amount)
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        ITransactionOutput txOutput = (await Transfer(pp, Utxo(1, 30_000_000), amount).Complete()).Body.Outputs().First();
        ITransactionOutput lowOutput = Low(pp, Utxo(1, 30_000_000), amount).Outputs[0];
        ITransactionOutput templateOutput = new OutputOptions { To = "p", Amount = Lovelace.FromAmount(amount) }
            .BuildOutput(new Dictionary<string, string> { ["p"] = Recipient }, pp.AdaPerUTxOByte!.Value);
        ITransactionOutput[] outputs = [txOutput, lowOutput, templateOutput];
        Assert.All(outputs, output => _ = Assert.IsType<AlonzoTransactionOutput>(output));
        foreach (ITransactionOutput output in outputs)
        {
            Assert.Equal(Math.Max(amount, FeeEngine.MinLovelace(output, pp.AdaPerUTxOByte!.Value)), output.Amount().Lovelace());
        }
    }

    [Fact]
    public void FixedFeeMustCoverItsOwnQuote()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        ResolvedInput input = Utxo(1, 30_000_000);
        const ulong fixedFee = 10;
        TransactionBuilder low = Low(pp, input, 2_000_000).SetFee(fixedFee);
        PostMaryTransaction atFixedFee = Sign(low.Build());
        ulong required = FeeEngine.LinearFee(DraftSize.EncodedLength(atFixedFee.Body) + DraftSize.LedgerOverhead(atFixedFee, 0), pp);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => low.CalculateFee([], defaultFee: fixedFee));
        Assert.Contains(required.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        ulong valid = FeeEngine.MinFee(atFixedFee, pp) + 1_000;
        _ = low.CalculateFee([], defaultFee: valid);
        Assert.Equal(valid, low.Fee);
    }

    [Theory]
    [InlineData(0UL, false)]
    [InlineData(300_000UL, true)]
    public void SuppliedAdaDustIsAbsorbedOnlyWhenFeeIsFree(ulong fixedFee, bool throws)
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        const ulong budget = 1_000_000;
        TransactionBuilder low = Low(pp, Utxo(1, 5_000_000), 2_000_000)
            .AddOutput(Recipient, Lovelace.FromAmount(budget), isChange: true);
        if (throws)
        {
            Assert.Throws<InvalidOperationException>(() => low.CalculateFee([], defaultFee: fixedFee));
        }
        else
        {
            _ = low.CalculateFee([]);
            Assert.Null(low.ChangeOutputIndex);
            Assert.Equal(budget, low.Fee);
        }
        PostAlonzoTransactionWitnessSet witnesses = Assert.IsType<PostAlonzoTransactionWitnessSet>(low.Build().Witnesses);
        Assert.Null(witnesses.VKeyWitnesses);
        Assert.Null(witnesses.BootstrapWitnesses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingSignaturesRejectedWithoutMutation(bool bootstrap)
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        TransactionBuilder low = Low(pp, Utxo(1, 5_000_000), 2_000_000).SetFee(300_000);
        _ = bootstrap ? low.AddBootstrapWitness(Bootstrap()) : low.AddVKeyWitness(VKeyWitness.Create(new byte[32], new byte[64]));
        byte[] before = CborSerializer.Serialize(low.Build());
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => low.CalculateFee([]));
        Assert.Equal("sign after the fee is set", error.Message);
        Assert.Equal(before, CborSerializer.Serialize(low.Build()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AShortPoolIsTriedInsteadOfTakenAsProofOfInsufficiency(bool tokens)
    {
        const ulong payment = 2_000_000;
        ProtocolParams pp = fixture.Pp(fixture.Row("F01")) with { MinFeeA = 0, MinFeeB = 200_000 };
        MultiAssetOutput assets = Assert.IsType<LovelaceWithMultiAsset>(fixture.Tx(fixture.Row("F03")).Body.Outputs().Last().Amount()).MultiAsset;
        IValue Value(ulong coin) => tokens ? LovelaceWithMultiAsset.Create(coin, assets) : Lovelace.FromAmount(coin);
        ITransactionOutput shape = TransactionBuilder.Create(pp).AddOutput(Recipient, Value(1_000_000)).Outputs[0];
        ulong min = FeeEngine.MinLovelace(shape, pp.AdaPerUTxOByte!.Value);
        ResolvedInput input = Utxo(1, payment + 200_000 + min - 2);
        input = new(input.Outref, PostAlonzoTransactionOutput.Create(PayAddress, Value(input.Output.Amount().Lovelace())));
        ResolvedInput[] pool = [Utxo(2, 1), Utxo(3, 1)];
        PostMaryTransaction tx = await Transfer(pp, input, payment).AddUnspentOutputs(pool).Complete();
        Assert.Equal(3, tx.Body.Inputs().Count());
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
        AssertConservation(tx, input.Output.Amount().Lovelace() + 2);
        if (tokens)
        {
            Assert.IsType<LovelaceWithMultiAsset>(tx.Body.Outputs().Last().Amount());
        }
        PostAlonzoTransactionWitnessSet ws = Assert.IsType<PostAlonzoTransactionWitnessSet>(tx.Witnesses);
        Assert.Null(ws.VKeyWitnesses);
        Assert.Null(ws.BootstrapWitnesses);
    }

    [Fact]
    public void NoBudgetStillSynchronizesCollateralIntegers()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F09"));
        ResolvedInput spend = Utxo(1, 5_000_000), collateral = Utxo(2, 2_000_000);
        TransactionBuilder b = Low(pp, spend, 2_000_000).AddCollateral(collateral.Outref).SetTotalCollateral(300_000)
            .SetCollateralReturn(PostAlonzoTransactionOutput.Create(PayAddress, Lovelace.FromAmount(1_700_000)));
        _ = b.CalculateFee([], resolvedInputs: [spend], availableInputs: [collateral]);
        PostMaryTransaction tx = b.Build();
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), b.Fee);
        ulong required = FeeEngine.RequiredCollateral(b.Fee, pp.CollateralPercentage!.Value);
        Assert.Equal(required, tx.Body.TotalCollateral());
        Assert.Equal(collateral.Output.Amount().Lovelace() - required, tx.Body.CollateralChange()!.Amount().Lovelace());
    }

    [Fact]
    public void TokenBearingChangeCannotBeAbsorbed()
    {
        JsonElement row = fixture.Row("F03");
        MultiAssetOutput assets = Assert.IsType<LovelaceWithMultiAsset>(fixture.Tx(row).Body.Outputs().Last().Amount()).MultiAsset;
        TransactionBuilder b = Low(fixture.Pp(row), Utxo(1, 5_000_000), 2_000_000);
        _ = b.AddOutput(PostAlonzoTransactionOutput.Create(PayAddress, LovelaceWithMultiAsset.Create(1, assets)));
        b.ChangeOutputIndex = 1;
        b.ChangeOutput = b.Outputs[1];
        Assert.Throws<InvalidOperationException>(() => b.CalculateFee([]));
        PostAlonzoTransactionWitnessSet ws = b.BuildWitnessSet();
        Assert.Null(ws.VKeyWitnesses);
        Assert.Null(ws.BootstrapWitnesses);
    }

    [Fact]
    public void NativeMintUsesOnlyKeysFromNeededScripts()
    {
        JsonElement row = fixture.Row("F11");
        ProtocolParams pp = fixture.Pp(row);
        ScriptPubKey mint = ScriptPubKey.Create(0, Enumerable.Repeat((byte)0x11, 28).ToArray());
        ScriptPubKey unused = ScriptPubKey.Create(0, Enumerable.Repeat((byte)0x22, 28).ToArray());
        ResolvedInput spend = NativeReference(Utxo(1, 5_000_000), mint), reference = NativeReference(Utxo(2, 5_000_000), unused);
        TransactionBuilder b = Low(pp, spend, 2_000_000).AddReferenceInput(reference.Outref)
            .AddMint(MintBuilder.Create().AddToken(mint.HashHex(), "00", 1).Build());
        _ = b.CalculateFee([], resolvedInputs: [spend, reference]);
        ulong size = FeeEngine.ReferenceScriptSize([spend.Outref], [reference.Outref], [spend, reference]);
        Assert.Equal(FeeEngine.MinFee(Sign(b.Build(), 2), pp, referenceScriptSize: size), b.Fee);
    }

    private static ResolvedInput NativeReference(ResolvedInput input, ScriptPubKey script) => new(input.Outref,
        PostAlonzoTransactionOutput.Create(PayAddress, input.Output.Amount(), scriptRef: new CborEncodedValue(CborSerializer.Serialize(MultiSigScript.Create(0, script)))));

    [Fact]
    public async Task ByronUsesTheInputAddressAttributes()
    {
        JsonElement row = fixture.Row("F01b");
        PostMaryTransaction seed = fixture.Tx(row);
        string inputPath = Text(row, "utxo");
        using JsonDocument entries = JsonDocument.Parse(fixture.Vectors.Bytes(inputPath));
        JsonElement entry = entries.RootElement.EnumerateObject().Single().Value;
        Address address = new(CborSerializer.Serialize(ByronAddressExtensions.FromBase58(Text(entry, "address"))));
        ResolvedInput input = new(seed.Body.Inputs().Single(), PostAlonzoTransactionOutput.Create(address,
            Lovelace.FromAmount(Number(entry.GetProperty("value"), "lovelace"))));
        ProtocolParams pp = fixture.Pp(row);
        PostMaryTransaction result = await Transfer(pp, input, 2_000_000).Complete();
        PostAlonzoTransactionWitnessSet ws = Assert.IsType<PostAlonzoTransactionWitnessSet>(result.Witnesses);
        BootstrapWitness real = Assert.Single(Assert.IsType<PostAlonzoTransactionWitnessSet>(seed.Witnesses).BootstrapWitnesses!.Value);
        PostMaryTransaction signed = PostMaryTransaction.Create(result.Body, PostAlonzoTransactionWitnessSet.Create(bootstrapWitnesses: CborList.Create([real], hasSetTag: true)),
            result.IsValid, result.AuxiliaryData);
        Assert.Null(ws.BootstrapWitnesses);
        Assert.Equal(FeeEngine.MinFee(signed, pp), result.Body.Fee());
    }

    [Fact]
    public void ReselectsCollateralEvenIfItsGrowthIsSmallerThanTheRequest()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F09")) with { MinFeeA = 0, MinFeeB = 200_000 };
        ulong total = FeeEngine.RequiredCollateral(pp.MinFeeB!.Value, pp.CollateralPercentage!.Value);
        ITransactionOutput template = new OutputBuilder(PayAddress.Value.ToArray(), Lovelace.FromAmount(1_000_000)).Build();
        ulong minReturn = FeeEngine.MinLovelace(template, pp.AdaPerUTxOByte!.Value);
        ResolvedInput spend = Utxo(1, 5_000_000), shortCollateral = Utxo(2, total + minReturn - 1), sufficient = Utxo(3, total + minReturn);
        TransactionBuilder b = Low(pp, spend, 2_000_000).AddCollateral(shortCollateral.Outref).SetTotalCollateral(total)
            .SetCollateralReturn(new OutputBuilder(PayAddress.Value.ToArray(), Lovelace.FromAmount(minReturn - 1)).Build());
        _ = b.CalculateFee([], resolvedInputs: [spend, shortCollateral], availableInputs: [sufficient]);
        PostMaryTransaction tx = b.Build();
        Assert.True(SameOutref(sufficient.Outref, Assert.Single(((ConwayTransactionBody)tx.Body).Collateral!.Value)));
        ITransactionOutput actual = tx.Body.CollateralChange()!;
        Assert.Equal(FeeEngine.MinLovelace(actual, pp.AdaPerUTxOByte!.Value), actual.Amount().Lovelace());
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), b.Fee);
    }

    [Fact]
    public async Task SpendingRepairRepricesWhenCollateralSetStaysTheSame()
    {
        (ProtocolParams parameters, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, _) = ScriptVector();
        ProtocolParams pp = parameters with { MinFeeA = 0, MinFeeB = 200_000, ExecutionCosts = ExUnitPrices.Create(new(0, 1), new(0, 1)) };
        ITransactionOutput model = new OutputBuilder(collateral.Output.Address().ToArray(), Lovelace.FromAmount(1_000_000)).Build();
        ulong minReturn = FeeEngine.MinLovelace(model, pp.AdaPerUTxOByte!.Value);
        ulong required = FeeEngine.RequiredCollateral(pp.MinFeeB!.Value, pp.CollateralPercentage!.Value);
        const ulong payment = 2_000_000;
        ResolvedInput spend = new(input.Outref, PostAlonzoTransactionOutput.Create(new Address(input.Output.Address()),
            Lovelace.FromAmount(payment + 200_000), Assert.IsType<PostAlonzoTransactionOutput>(input.Output).Datum));
        ResolvedInput tight = new(collateral.Outref, PostAlonzoTransactionOutput.Create(new Address(collateral.Output.Address()),
            Lovelace.FromAmount(required + minReturn)));
        TransactionBuilder draftBuilder = Low(pp, spend, payment).AddCollateral(tight.Outref).SetTotalCollateral(0)
            .SetCollateralReturn(new OutputBuilder(tight.Output.Address().ToArray(), tight.Output.Amount()).Build())
            .AddOutput(Recipient, Lovelace.FromAmount(200_000), isChange: true);
        FeeDraft draft = TransactionBuilderExtensions.PrepareStructure(draftBuilder, [spend, tight], new FeeInputs(Budget: 200_000), 0);
        Assert.Equal(FeeStatus.SpendingShort | FeeStatus.CollateralShort, FeeSolver.Solve(draft).Status);
        ResolvedInput additional = Utxo(0xf1, 1_000_000);
        PostMaryTransaction tx = await ScriptSpend(pp, spend, tight, script, (_, _) => Result(0, 0))
            .AddUnspentOutputs([additional]).Complete();
        Assert.Contains(tx.Body.Inputs(), i => SameOutref(i, additional.Outref));
        Assert.True(SameOutref(tight.Outref, Assert.Single(((ConwayTransactionBody)tx.Body).Collateral!.Value)));
        ITransactionOutput returned = tx.Body.CollateralChange()!;
        Assert.Equal(FeeEngine.MinLovelace(returned, pp.AdaPerUTxOByte!.Value), returned.Amount().Lovelace());
        Assert.Equal(FeeEngine.MinFee(Sign(tx, 2), pp), tx.Body.Fee());
        AssertConservation(tx, spend.Output.Amount().Lovelace() + additional.Output.Amount().Lovelace());
    }

    [Fact]
    public async Task ScriptTemplateSettlesOnItsSignedQuote()
    {
        (ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, ExUnits units) = ScriptVector();
        string payer = CliSnapshotProvider.Bech32(collateral.Output);
        int calls = 0;
        TransactionTemplate<int> template = TransactionTemplateBuilder.Create<int>(new CliSnapshotProvider(pp, [collateral]))
            .SetChangeAddress(payer)
            .AddInput((options, ignored) =>
            {
                options.Id = "spent";
                options.Utxo = input;
                _ = options.SetRedeemerBuilder(_ => PlutusInt.Create(42));
            })
            .AddOutput((options, _, _) => { options.To = "change"; options.Amount = Lovelace.FromAmount(2_000_000); })
            .SetPreBuildHook((builder, _, _) => builder.AddPlutusScript(script))
            .UseEvaluator((evaluated, resolved) =>
            {
                _ = resolved.ToDictionary(u => (Convert.ToHexStringLower(u.Outref.TransactionId.Span), u.Outref.Index));
                return Result(++calls == 1 ? units.Mem : units.Mem + 1, units.Steps);
            })
            .Build();
        PostMaryTransaction tx = (PostMaryTransaction)await template(0);
        Assert.Equal(3, calls);
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
    }

    [Fact]
    public void CalculateFeeFailsWhenSuppliedCollateralPoolCannotMeetMinimumReturn()
    {
        (ProtocolParams pp, ResolvedInput spend, _, PlutusV3Script script, _) = ScriptVector();
        ulong minReturn = FeeEngine.MinLovelace(new OutputBuilder(PayAddress.Value.ToArray(), Lovelace.FromAmount(1_000_000)).Build(), pp.AdaPerUTxOByte!.Value);
        ResolvedInput tooSmall = Utxo(0xc1, minReturn - 1);
        TransactionBuilder b = Low(pp, spend, 2_000_000)
            .AddPlutusScript(script)
            .SetRedeemers(RedeemerMap.Create(new Dictionary<RedeemerKey, RedeemerValue>
            {
                [RedeemerKey.Create(0, 0)] = RedeemerValue.Create(PlutusInt.Create(42), ExUnits.Create(0, 0))
            }));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            b.CalculateFee([script], resolvedInputs: [spend], availableInputs: [tooSmall]));
        Assert.Equal("Insufficient collateral funds.", error.Message);
    }

    [Theory]
    [InlineData(1, 200_000UL)]
    [InlineData(3, 1_500_000UL)]
    public async Task CollateralRepairFallsBackToTokenBearingInputWhenAdaOnlyFails(int adaCount, ulong fee)
    {
        (ProtocolParams parameters, ResolvedInput input, _, PlutusV3Script script, _) = ScriptVector();
        ProtocolParams pp = parameters with { MinFeeA = 0, MinFeeB = fee, MaxCollateralInputs = 3, ExecutionCosts = ExUnitPrices.Create(new(0, 1), new(0, 1)) };
        MultiAssetOutput assets = Assert.IsType<LovelaceWithMultiAsset>(fixture.Tx(fixture.Row("F03")).Body.Outputs().Last().Amount()).MultiAsset;
        List<ResolvedInput> adaOnly = [.. Enumerable.Range(0, adaCount).Select(i => Utxo((byte)(0xc2 + i), 1_000_000))];
        ResolvedInput tokenBearing = new(Utxo(0xc5, 0).Outref, PostAlonzoTransactionOutput.Create(PayAddress, LovelaceWithMultiAsset.Create(10_000_000, assets)));
        List<ResolvedInput> collateralPool = [.. adaOnly, tokenBearing];
        PostMaryTransaction tx = await new TxBuilder(new CliSnapshotProvider(pp, [input, .. collateralPool]))
            .AddInput(input, PlutusInt.Create(42))
            .PayLovelace(Recipient, 2_000_000)
            .SetChangeAddress(Recipient)
            .ProvideScript(script)
            .ProvideCollateral(collateralPool)
            .UseEvaluator((_, _) => Result(0, 0))
            .Complete();
        Assert.True(SameOutref(tokenBearing.Outref, Assert.Single(((ConwayTransactionBody)tx.Body).Collateral!.Value)));
        ITransactionOutput returned = tx.Body.CollateralChange()!;
        Assert.Equal(assets.Raw, Assert.IsType<LovelaceWithMultiAsset>(returned.Amount()).MultiAsset.Raw);
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
    }

    [Fact]
    public async Task AutoSelectedCollateralPoolIsNotMutatedBySpendingCoinSelection()
    {
        (ProtocolParams parameters, ResolvedInput input, _, PlutusV3Script script, _) = ScriptVector();
        ProtocolParams pp = parameters with { MinFeeA = 0, MinFeeB = 400_000, ExecutionCosts = ExUnitPrices.Create(new(0, 1), new(0, 1)) };
        ResolvedInput spend = new(input.Outref, PostAlonzoTransactionOutput.Create(new Address(input.Output.Address()),
            Lovelace.FromAmount(1_500_000), Assert.IsType<PostAlonzoTransactionOutput>(input.Output).Datum));
        ResolvedInput u1 = Utxo(0xd1, 1_300_000), u2 = Utxo(0xd2, 1_300_000);
        PostMaryTransaction tx = await new TxBuilder(new CliSnapshotProvider(pp, [spend, u1, u2]))
            .AddInput(spend, PlutusInt.Create(42))
            .AddUnspentOutputs([u1, u2])
            .PayLovelace(Recipient, 2_000_000)
            .SetChangeAddress(Recipient)
            .ProvideScript(script)
            .UseEvaluator((_, _) => Result(0, 0))
            .Complete();
        Assert.Equal(2, ((ConwayTransactionBody)tx.Body).Collateral!.Value.Count);
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
        AssertConservation(tx, 4_100_000);
    }

    [Fact]
    public async Task TemplateSerializesFreshCustomCborSerializableRedeemer()
    {
        (ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, ExUnits units) = ScriptVector();
        string payer = CliSnapshotProvider.Bech32(collateral.Output);
        TransactionTemplate<int> template = TransactionTemplateBuilder.Create<int>(new CliSnapshotProvider(pp, [collateral]))
            .SetChangeAddress(payer)
            .AddInput((options, ignored) =>
            {
                options.Id = "spent";
                options.Utxo = input;
                _ = options.SetRedeemerBuilder(_ => new OutputReference(new TransactionId(new byte[32]), 7));
            })
            .AddOutput((options, _, _) => { options.To = "change"; options.Amount = Lovelace.FromAmount(2_000_000); })
            .SetPreBuildHook((builder, _, _) => builder.AddPlutusScript(script))
            .UseEvaluator((_, _) => Result(units.Mem, units.Steps))
            .Build();
        PostMaryTransaction tx = (PostMaryTransaction)await template(0);
        RedeemerMap redeemers = Assert.IsType<RedeemerMap>(Assert.IsType<PostAlonzoTransactionWitnessSet>(tx.Witnesses).Redeemers);
        PlutusConstr data = Assert.IsType<PlutusConstr>(Assert.Single(redeemers.Value).Value.Data);
        Assert.Equal(7UL, Assert.IsType<PlutusUint64>(data.Fields.ElementAt(1)).Value);
        Assert.Equal(FeeEngine.MinFee(Sign(tx), pp), tx.Body.Fee());
    }

    [Fact]
    public async Task ScriptEvaluationSettlesAfterStructureRepairReturnsToEarlierUnits()
    {
        (ProtocolParams parameters, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, _) = ScriptVector();
        ProtocolParams pp = parameters with { MinFeeA = 0, MinFeeB = 200_000, ExecutionCosts = ExUnitPrices.Create(new(1, 1), new(0, 1)) };
        const ulong payment = 2_000_000, u1 = 10_000, u2 = 20_000;
        ulong minChange = FeeEngine.MinLovelace(new OutputBuilder(Recipient, Lovelace.FromAmount(1_000_000)).Build(), pp.AdaPerUTxOByte!.Value);
        ResolvedInput spend = new(input.Outref, PostAlonzoTransactionOutput.Create(new Address(input.Output.Address()),
            Lovelace.FromAmount(payment + 200_000 + u1 + minChange), Assert.IsType<PostAlonzoTransactionOutput>(input.Output).Datum));
        ResolvedInput bigCollateral = Utxo(0xe1, 10_000_000);
        PostMaryTransaction tx = await ScriptSpend(pp, spend, bigCollateral, script, (candidate, _) =>
            Result(candidate.Body.Outputs().Count() == 2 ? u2 : u1, 0)).Complete();
        Assert.Single(tx.Body.Outputs());
        Assert.Equal(200_000 + u1 + minChange, tx.Body.Fee());
        RedeemerMap redeemers = Assert.IsType<RedeemerMap>(Assert.IsType<PostAlonzoTransactionWitnessSet>(tx.Witnesses).Redeemers);
        Assert.Equal(u1, Assert.Single(redeemers.Value).Value.ExUnits.Mem);
    }

    [Fact]
    public void FixedFeeAcceptsExactZeroChangeBudget()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        const ulong payment = 2_000_000, fixedFee = 300_000;
        ResolvedInput input = Utxo(0xe2, payment + fixedFee);
        TransactionBuilder fromChange = Low(pp, input, payment);
        _ = fromChange.CalculateFee([], defaultFee: fixedFee, changeAddress: Recipient, resolvedInputs: [input]);
        Assert.Null(fromChange.ChangeOutputIndex);
        Assert.Equal(fixedFee, fromChange.Fee);
        AssertConservation(fromChange.Build(), payment + fixedFee);
        const ulong suppliedFee = 1_000_000;
        TransactionBuilder supplied = Low(pp, Utxo(0xe2, payment + suppliedFee), payment).AddOutput(Recipient, Lovelace.FromAmount(suppliedFee), isChange: true);
        _ = supplied.CalculateFee([], defaultFee: suppliedFee);
        Assert.Null(supplied.ChangeOutputIndex);
        Assert.Equal(suppliedFee, supplied.Fee);
        AssertConservation(supplied.Build(), payment + suppliedFee);
        Assert.Throws<InvalidOperationException>(() => Low(pp, Utxo(0xe3, payment + 10), payment)
            .CalculateFee([], defaultFee: 10, changeAddress: Recipient, resolvedInputs: [Utxo(0xe3, payment + 10)]));
    }

    [Fact]
    public void CertificatesVotersAndProposalPoliciesAffectWitnessRequirements()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        ResolvedInput spend = Utxo(0xe4, 10_000_000);
        static byte[] Key(byte b) => [.. Enumerable.Repeat(b, 28)];
        ScriptPubKey policyScript = ScriptPubKey.Create(0, Key(0x77));
        PoolRegistration poolReg = PoolRegistration.Create(3, Key(0x22), new byte[32], 0, 0, new CborRationalNumber(0, 1),
            new RewardAccount((byte[])[0xe0, .. new byte[28]]), CborList.Create<ReadOnlyMemory<byte>>([Key(0x33)]), CborList.Create<IRelay>([]), null);
        VotingProcedures votes = new(new Dictionary<Voter, GovActionIdVotingProcedure>
        {
            [Voter.Create(2, Key(0x55))] = new(new Dictionary<GovActionId, VotingProcedure>
            {
                [GovActionId.Create(new byte[32], 0)] = VotingProcedure.Create(1, null)
            }),
            [Voter.Create(3, Key(0x66))] = new(new Dictionary<GovActionId, VotingProcedure>
            {
                [GovActionId.Create(new byte[32], 0)] = VotingProcedure.Create(1, null)
            })
        });
        ProposalProcedure proposal = ProposalProcedure.Create(0, new RewardAccount((byte[])[0xe0, .. new byte[28]]),
            TreasuryWithdrawalsAction.Create(2, [], Convert.FromHexString(policyScript.HashHex())),
            Anchor.Create("https://example.invalid", new byte[32]));
        TransactionBuilder b = Low(pp, spend, 2_000_000)
            .AddCertificate(StakeDelegation.Create(2, Credential.Create(0, Key(0x11)), Key(0x99)))
            .AddCertificate(poolReg)
            .AddCertificate(PoolRetirement.Create(4, Key(0x44), 10))
            .SetVotingProcedures(votes)
            .AddProposalProcedure(proposal)
            .AddNativeScript(policyScript);
        _ = b.CalculateFee([], resolvedInputs: [spend]);
        Assert.Equal(FeeEngine.MinFee(Sign(b.Build(), 7), pp), b.Fee);
    }

    [Fact]
    public async Task ReindexPreservesUnitsWhenCoinSelectionShiftsScriptInputIndex()
    {
        (ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, _) = ScriptVector();
        ResolvedInput scriptInput = new(TransactionInput.Create((byte[])[0x20, .. new byte[31]], 0),
            PostAlonzoTransactionOutput.Create(new Address(input.Output.Address()), Lovelace.FromAmount(1_000_000),
                Assert.IsType<PostAlonzoTransactionOutput>(input.Output).Datum));
        ResolvedInput earlierKey = Utxo(0x01, 5_000_000);
        const ulong expectedMem = 123_456, expectedSteps = 654_321;
        bool sawShiftedUnits = false;
        PostMaryTransaction tx = await ScriptSpend(pp, scriptInput, collateral, script, (candidate, _) =>
        {
            if (candidate.Body.Inputs().Count() == 1)
            {
                return Result(expectedMem, expectedSteps);
            }
            RedeemerMap map = Assert.IsType<RedeemerMap>(Assert.IsType<PostAlonzoTransactionWitnessSet>(candidate.Witnesses).Redeemers);
            KeyValuePair<RedeemerKey, RedeemerValue> entry = Assert.Single(map.Value);
            Assert.Equal(1UL, entry.Key.Index);
            sawShiftedUnits = entry.Value.ExUnits.Mem == expectedMem && entry.Value.ExUnits.Steps == expectedSteps;
            return Task.FromResult<IReadOnlyList<EvaluationResult>>([new(0, 1, new(expectedMem, expectedSteps))]);
        }).AddUnspentOutputs([earlierKey]).Complete();
        Assert.True(sawShiftedUnits);
        Assert.Equal(FeeEngine.MinFee(Sign(tx, 2), pp), tx.Body.Fee());
    }

    // F03 matches because its input's tokens are read in the ledger's order (CliUtxo.ReadValue), and a change keeps its inputs' token order.
    [Theory]
    [InlineData("F01")]
    [InlineData("F03")]
    [InlineData("B1")]
    [InlineData("B2")]
    [InlineData("B5")]
    public async Task TransfersWriteTheCliBytes(string id)
    {
        JsonElement row = fixture.Row(id);
        ResolvedInput input = Assert.Single(fixture.Inputs(row));
        ITransactionOutput[] outputs = [.. fixture.Tx(row).Body.Outputs()];
        PostMaryTransaction built = await new TxBuilder(new CliSnapshotProvider(fixture.Pp(row), [input])).AddInput(input)
            .PayAssets(CliSnapshotProvider.Bech32(outputs[0]), outputs[0].Amount()).SetChangeAddress(CliSnapshotProvider.Bech32(outputs[^1])).Complete();
        Assert.Equal(Convert.ToHexStringLower(fixture.Vectors.Bytes(Text(row, "tx"))), Hex(built));
    }

    [Fact]
    public async Task TemplateTransferWritesTheCliBytes()
    {
        JsonElement row = fixture.Row("F01");
        ResolvedInput input = Assert.Single(fixture.Inputs(row));
        TransactionTemplate<int> template = TransactionTemplateBuilder.Create<int>(new CliSnapshotProvider(fixture.Pp(row), [input]))
            .SetChangeAddress(CliSnapshotProvider.Bech32(input.Output))
            .AddOutput((options, _, _) => { options.To = "change"; options.Amount = Lovelace.FromAmount(2_000_000); })
            .Build();
        Assert.Equal(Convert.ToHexStringLower(fixture.Vectors.Bytes(Text(row, "tx"))), Hex(await template(0)));
    }

    [Fact]
    public async Task ScriptSpendWritesTheCliOutputsAndCollateralReturn()
    {
        (ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, PlutusV3Script script, ExUnits units) = ScriptVector();
        PostMaryTransaction stored = fixture.Tx(fixture.Row("F09"));
        PostMaryTransaction tx = await ScriptSpend(pp, input, collateral, script, (_, _) => Result(units.Mem, units.Steps),
            CliSnapshotProvider.Bech32(collateral.Output)).Complete();
        PostMaryTransaction built = (PostMaryTransaction)ITransaction.Read(CborSerializer.Serialize(tx), Era.Conway);
        Assert.Equal(Parts(stored), Parts(built));
    }

    [Fact]
    public async Task HandedInOutputsKeepTheirBytes()
    {
        ProtocolParams pp = fixture.Pp(fixture.Row("F01"));
        PostAlonzoTransactionOutput handed = PostAlonzoTransactionOutput.Create(PayAddress, Lovelace.FromAmount(2_000_000));
        ResolvedInput input = Utxo(1, 30_000_000);
        PostMaryTransaction tx = await new TxBuilder(new CliSnapshotProvider(pp, [input])).AddInput(input).SetChangeAddress(Recipient).AddOutput(handed).Complete();
        Assert.Equal(Hex(handed), Hex(tx.Body.Outputs().First()));
        _ = Assert.IsType<AlonzoTransactionOutput>(tx.Body.Outputs().Last());
    }

    [Fact]
    public void AHashNextToAReferenceScriptKeepsTheMapForm()
    {
        (_, _, _, PlutusV3Script script, _) = ScriptVector();
        ITransactionOutput built = new OutputBuilder(PayAddress.Value.ToArray(), Lovelace.FromAmount(2_000_000)).WithDatumHash(new byte[32])
            .WithScriptRef(script).Build();
        _ = Assert.IsType<DatumHashOption>(Assert.IsType<PostAlonzoTransactionOutput>(built).Datum);
    }

    private static readonly Address PayAddress = Address.Create(new byte[29] { 0x60, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
    private static readonly string Recipient = WalletAddress.FromBytes(PayAddress.Value.ToArray()).ToBech32();
    private static readonly string OtherRecipient = WalletAddress.FromBytes([0x60, .. Enumerable.Repeat((byte)2, 28)]).ToBech32();

    internal static PostMaryTransaction Sign(PostMaryTransaction tx, int count = 1) => (PostMaryTransaction)tx.Sign(
        [.. Enumerable.Range(1, count).Select(i => new PrivateKey([.. Enumerable.Repeat((byte)i, 32)], new byte[32]))]);
    private static BootstrapWitness Bootstrap() => BootstrapWitness.Create(new byte[32], new byte[64], new byte[32], (byte[])[0xa0]);
    private static bool SameOutref(TransactionInput a, TransactionInput b) => a.Index == b.Index && a.TransactionId.Span.SequenceEqual(b.TransactionId.Span);
    private static ResolvedInput Utxo(byte id, ulong coin) => new(TransactionInput.Create((byte[])[.. Enumerable.Repeat(id, 32)], 0),
        PostAlonzoTransactionOutput.Create(PayAddress, Lovelace.FromAmount(coin)));
    private static TransactionBuilder Low(ProtocolParams pp, ResolvedInput input, ulong payment) => TransactionBuilder.Create(pp)
        .AddInput(input.Outref).AddOutput(Recipient, Lovelace.FromAmount(payment)).SetFee(200_000);
    private static TxBuilder Transfer(ProtocolParams pp, ResolvedInput input, ulong payment) => new TxBuilder(new CliSnapshotProvider(pp, [input]))
        .AddInput(input).SetChangeAddress(Recipient).PayLovelace(Recipient, payment);
    private static void AssertConservation(PostMaryTransaction tx, ulong input) => Assert.Equal(input,
        tx.Body.Outputs().Aggregate(tx.Body.Fee(), (sum, output) => sum + output.Amount().Lovelace()));
    private static string Hex<T>(T value) where T : ICborType => Convert.ToHexStringLower(CborSerializer.Serialize(value));
    private static (string, string?, ulong, ulong?) Parts(PostMaryTransaction x) => (string.Join('|', x.Body.Outputs().Select(output => Convert.ToHexStringLower(output.Raw.Span))),
        ((ConwayTransactionBody)x.Body).CollateralReturn is { } r ? Convert.ToHexStringLower(r.Raw.Span) : null, x.Body.Fee(), x.Body.TotalCollateral());

    private (ProtocolParams, ResolvedInput, ResolvedInput, PlutusV3Script, ExUnits) ScriptVector()
    {
        JsonElement row = fixture.Row("F09");
        PostMaryTransaction seed = fixture.Tx(row);
        ResolvedInput[] utxos = fixture.Inputs(row);
        ResolvedInput spend = utxos.Single(u => SameOutref(u.Outref, seed.Body.Inputs().Single()));
        ResolvedInput collateral = utxos.Single(u => !SameOutref(u.Outref, spend.Outref));
        ReadOnlyMemory<byte> program = Assert.Single(((PostAlonzoTransactionWitnessSet)seed.Witnesses).PlutusV3Scripts!.Value);
        ITransactionOutput output = PostAlonzoTransactionOutput.Create(new Address(spend.Output.Address()), spend.Output.Amount(),
            DatumOptionExtensions.InlineDatumFrom(PlutusInt.Create(42)));
        RedeemerMap redeemers = Assert.IsType<RedeemerMap>(((PostAlonzoTransactionWitnessSet)seed.Witnesses).Redeemers);
        return (fixture.Pp(row), new ResolvedInput(spend.Outref, output), collateral, PlutusV3Script.Create(3, program), Assert.Single(redeemers.Value).Value.ExUnits);
    }

    // The ledger's script integrity hash: redeemers and datums as the transaction carries them, then one V2 or V3 language view,
    // {language: its costs as a fixed-length list, whose head is 0x99 for 256 to 65,535 costs} (cardano-ledger 95b369053f, Alonzo/PParams.hs:549-552, 566-574).
    private static byte[] LedgerScriptDataHash(PostMaryTransaction tx, byte language, CborList<long>? costModel)
    {
        PostAlonzoTransactionWitnessSet witnesses = Assert.IsType<PostAlonzoTransactionWitnessSet>(tx.Witnesses);
        long[] costs = [.. costModel ?? throw new InvalidOperationException("No cost model.")];
        Assert.InRange(costs.Length, 256, ushort.MaxValue);
        byte[] items = CborSerializer.Serialize(CborList.Create(costs, isIndefinite: true))[1..^1];
        byte[] datums = witnesses.PlutusDataSet is { Count: > 0 } set ? set.Raw.ToArray() : [];
        return HashUtil.Blake2b256([.. witnesses.Redeemers?.Raw.ToArray() ?? [], .. datums, 0xa1, language, 0x99, (byte)(costs.Length >> 8), (byte)costs.Length, .. items]);
    }

    private static TxBuilder ScriptSpend(ProtocolParams pp, ResolvedInput input, ResolvedInput collateral, IScript script,
        Func<PostMaryTransaction, IReadOnlyList<ResolvedInput>, Task<IReadOnlyList<EvaluationResult>>> evaluate, string? to = null) =>
        new TxBuilder(new CliSnapshotProvider(pp, [input, collateral])).AddInput(input, PlutusInt.Create(42)).PayLovelace(to ?? Recipient, 2_000_000)
            .SetChangeAddress(to ?? Recipient).ProvideScript(script).ProvideCollateral([collateral]).UseEvaluator(evaluate);
    private static Task<IReadOnlyList<EvaluationResult>> Result(ulong mem, ulong steps) => Task.FromResult<IReadOnlyList<EvaluationResult>>([new(0, 0, new(mem, steps))]);

}

public sealed class BuilderFeeFixture
{
    internal CliVectors Vectors { get; } = Load(CliVectors.Root);
    internal JsonElement Row(string id, int index = 0) => Vectors.Cases.Single(item => Text(item, "id") == id).GetProperty("rows")[index];
    internal PostMaryTransaction Tx(JsonElement row) => (PostMaryTransaction)ITransaction.Read(Vectors.Bytes(Text(row, "tx")), Era.Conway);
    internal ProtocolParams Pp(JsonElement row) => Vectors.Parameters(Text(row, "parameters"));
    internal ResolvedInput[] Inputs(JsonElement row) => CliUtxo.Read(Vectors, Text(row, "utxo"));

    public BuilderFeeFixture()
    {
        JsonElement row = Row("F01");
        PostMaryTransaction list = Tx(row);
        ulong l = Number(row, "q");
        TransactionBuilder control = TransactionBuilder.Create(Pp(row)).AddInput(list.Body.Inputs().Single()).SetFee(l);
        foreach (ITransactionOutput output in list.Body.Outputs())
        {
            _ = control.AddOutput(PostAlonzoTransactionOutput.Create(new Address(output.Address()), output.Amount()));
        }
        Assert.Equal(l + 176, FeeEngine.MinFee(BuilderFeeTests.Sign(control.Build()), Pp(row)));
    }
}

using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Xunit;

namespace Chrysalis.Tx.Test;

public sealed class ScriptEvaluationTests
{
    private static readonly byte[] AddressBytes = [0x60, .. new byte[28]];
    private static readonly ResolvedInput Funds = new(TransactionInput.Create(new byte[32], 0),
        PostAlonzoTransactionOutput.Create(new Address(AddressBytes), Lovelace.Create(5_000_000)));

    [Fact]
    public void EvaluateRejectsANonUnitV3MintResult()
    {
        TransactionBuilder builder = Mint(3, "(con integer 1)");
        Assert.IsType<InvalidReturnValueException>(Assert.Throws<InvalidOperationException>(() => builder.Evaluate([Funds], SlotNetworkConfig.Preview)).InnerException);
    }

    [Fact]
    public void EvaluateSetsNonZeroUnitsForAV3UnitMintResult()
    {
        TransactionBuilder builder = Mint(3, "(con unit ())").Evaluate([Funds], SlotNetworkConfig.Preview);
        RedeemerMap map = Assert.IsType<RedeemerMap>(builder.BuildWitnessSet().Redeemers);
        RedeemerValue redeemer = Assert.Single(map.Value).Value;
        Assert.True(redeemer.ExUnits.Mem > 0 && redeemer.ExUnits.Steps > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EvaluateStillRefusesV1AndV2Contexts(int language)
    {
        TransactionBuilder builder = Mint(language, "(lam ctx (con unit ()))");
        Assert.IsType<NotSupportedException>(Assert.Throws<InvalidOperationException>(() => builder.Evaluate([Funds], SlotNetworkConfig.Preview)).InnerException);
    }

    [Fact]
    public void EvaluateTxPassesTheRequestedProtocolVersion()
    {
        TransactionBuilder builder = Mint(3, "(con unit ())");
        Assert.IsType<NotSupportedException>(Assert.Throws<InvalidOperationException>(() => ScriptContextBuilder.EvaluateTx(
            builder.BuildBody(), builder.BuildWitnessSet(), [Funds], SlotNetworkConfig.Preview, protocolVersion: int.MaxValue)).InnerException);
    }

    [Fact]
    public void TxInfoTimesAValidityStartBeforeShelleyAsTheLedgerDoes()
    {
        TransactionBuilder builder = Mint(3, "(con unit ())").SetValidityIntervalStart(0).SetTtl(86_500);
        PlutusDataConstr info = Assert.IsType<PlutusDataConstr>(ScriptContextBuilder.BuildTxInfoV3(builder.BuildBody(), builder.BuildWitnessSet(), [Funds], SlotNetworkConfig.Preprod));
        PlutusDataConstr range = Assert.IsType<PlutusDataConstr>(info.Fields[7]);
        Assert.Equal(1_654_041_600_000, Bound(range, 0));
        Assert.Equal(1_655_769_700_000, Bound(range, 1));
    }

    // The time of a finite bound: side 0 is the lower bound, side 1 the upper; each is Constr 0 [Constr 1 [time], closed].
    private static long Bound(PlutusDataConstr range, int side) =>
        (long)Assert.IsType<PlutusDataInteger>(Assert.Single(Assert.IsType<PlutusDataConstr>(Assert.IsType<PlutusDataConstr>(range.Fields[side]).Fields[0]).Fields)).Value;

    private static TransactionBuilder Mint(int language, string result)
    {
        byte[] wrapped = TestScripts.Wrap($"(program 1.0.0 (lam redeemer {result}))");
        IScript script = language switch
        {
            1 => PlutusV1Script.Create(1, wrapped),
            2 => PlutusV2Script.Create(2, wrapped),
            _ => PlutusV3Script.Create(3, wrapped)
        };
        const string asset = "00";
        string policy = script.HashHex();
        return new TransactionBuilder()
            .AddInput(Funds.Outref)
            .AddOutput(AddressBytes, LovelaceWithMultiAsset.Create(4_800_000, TestScripts.OneToken(policy)))
            .SetFee(200_000)
            .AddMint(policy, asset, 1, script, PlutusInt.Create(0));
    }
}

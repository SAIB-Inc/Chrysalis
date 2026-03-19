using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Builders;
using Xunit;

namespace Chrysalis.Tx.Test;

/// <summary>
/// Tests ported from CML's redeemer_builder.rs — verifies deferred index computation
/// assigns correct indices based on sorted input order.
/// </summary>
public class RedeemerBuilderTests
{
    private static TransactionInput MakeInput(byte hashByte, ulong index)
    {
        byte[] hash = new byte[32];
        hash[0] = hashByte;
        return TransactionInput.Create(hash, index);
    }

    private static AlonzoTransactionOutput MakeOutput() =>
        AlonzoTransactionOutput.Create(
            Address.Create(new byte[57]),
            Lovelace.Create(0),
            null);

    private static PlutusInt MakeRedeemer(int value) =>
        PlutusInt.Create(value);

    private static bool HasKey(RedeemerMap map, int tag, ulong index) =>
        map.Value.Any(kv => kv.Key.Tag == tag && kv.Key.Index == index);

    private static RedeemerValue GetValue(RedeemerMap map, int tag, ulong index) =>
        map.Value.First(kv => kv.Key.Tag == tag && kv.Key.Index == index).Value;

    [Fact]
    public void SpendRedeemerGetsCorrectIndexFromSortedInputOrder()
    {
        RedeemerBuilder builder = new();

        builder.AddSpend(new InputBuilderResult(MakeInput(1, 1), MakeOutput(), new WitnessRequirements()));
        builder.AddSpend(new InputBuilderResult(MakeInput(1, 0), MakeOutput(), new WitnessRequirements()));

        WitnessRequirements reqs = new() { RedeemerData = MakeRedeemer(42) };
        builder.AddSpend(new InputBuilderResult(MakeInput(0, 0), MakeOutput(), reqs));

        IRedeemers redeemers = builder.Build();
        RedeemerMap map = Assert.IsType<RedeemerMap>(redeemers);

        Assert.Single(map.Value);
        Assert.True(HasKey(map, 0, 0)); // Spend tag=0, Index=0 (sorts first)
    }

    [Fact]
    public void MultipleSpendRedeemersGetSequentialIndices()
    {
        RedeemerBuilder builder = new();

        builder.AddSpend(new InputBuilderResult(MakeInput(2, 0), MakeOutput(), new() { RedeemerData = MakeRedeemer(3) }));
        builder.AddSpend(new InputBuilderResult(MakeInput(1, 0), MakeOutput(), new() { RedeemerData = MakeRedeemer(2) }));
        builder.AddSpend(new InputBuilderResult(MakeInput(0, 0), MakeOutput(), new() { RedeemerData = MakeRedeemer(1) }));

        IRedeemers redeemers = builder.Build();
        RedeemerMap map = Assert.IsType<RedeemerMap>(redeemers);

        Assert.Equal(3, map.Value.Count);
        Assert.True(HasKey(map, 0, 0));
        Assert.True(HasKey(map, 0, 1));
        Assert.True(HasKey(map, 0, 2));
    }

    [Fact]
    public void MintRedeemersGetSortedPolicyIndices()
    {
        RedeemerBuilder builder = new();

        builder.AddMint("CC", MakeRedeemer(2));
        builder.AddMint("AA", MakeRedeemer(1));
        builder.AddMint("BB", MakeRedeemer(3));

        IRedeemers redeemers = builder.Build();
        RedeemerMap map = Assert.IsType<RedeemerMap>(redeemers);

        Assert.Equal(3, map.Value.Count);
        Assert.True(HasKey(map, 1, 0));
        Assert.True(HasKey(map, 1, 1));
        Assert.True(HasKey(map, 1, 2));
    }

    [Fact]
    public void UpdateExUnitsAppliesCorrectly()
    {
        RedeemerBuilder builder = new();

        builder.AddSpend(new InputBuilderResult(MakeInput(0, 0), MakeOutput(), new() { RedeemerData = MakeRedeemer(1) }));
        builder.UpdateExUnits(0, 0, ExUnits.Create(1000, 2000));

        IRedeemers redeemers = builder.Build();
        RedeemerMap map = Assert.IsType<RedeemerMap>(redeemers);

        RedeemerValue val = GetValue(map, 0, 0);
        Assert.Equal(1000UL, val.ExUnits.Mem);
        Assert.Equal(2000UL, val.ExUnits.Steps);
    }

    [Fact]
    public void HasRedeemersReturnsFalseWhenEmpty() =>
        Assert.False(new RedeemerBuilder().HasRedeemers);

    [Fact]
    public void HasRedeemersReturnsTrueAfterAddingSpend()
    {
        RedeemerBuilder builder = new();
        builder.AddSpend(new InputBuilderResult(MakeInput(0, 0), MakeOutput(), new() { RedeemerData = MakeRedeemer(1) }));
        Assert.True(builder.HasRedeemers);
    }
}

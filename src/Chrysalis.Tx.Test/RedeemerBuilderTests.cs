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

    /// <summary>
    /// CML test: redeemers get indices from sorted input order.
    /// Input [0x00]#0 sorts before [0x01]#0 and [0x01]#1.
    /// Only the input with a redeemer gets an entry.
    /// </summary>
    [Fact]
    public void SpendRedeemerGetsCorrectIndexFromSortedInputOrder()
    {
        RedeemerBuilder builder = new();

        // Add input [0x01]#1 (no redeemer — just a placeholder)
        InputBuilderResult input1 = new(MakeInput(1, 1), MakeOutput(), new WitnessRequirements());
        builder.AddSpend(input1);

        // Add input [0x01]#0 (no redeemer)
        InputBuilderResult input2 = new(MakeInput(1, 0), MakeOutput(), new WitnessRequirements());
        builder.AddSpend(input2);

        // Add input [0x00]#0 (with redeemer — this should be index 0 after sorting)
        WitnessRequirements reqs = new() { RedeemerData = MakeRedeemer(42) };
        InputBuilderResult input3 = new(MakeInput(0, 0), MakeOutput(), reqs);
        builder.AddSpend(input3);

        IRedeemers redeemers = builder.Build();
        RedeemerList list = Assert.IsType<RedeemerList>(redeemers);

        // Only one redeemer (the one with data)
        Assert.Single(list.Value);

        RedeemerEntry entry = list.Value[0];
        Assert.Equal(0, entry.Tag);   // Spend tag
        Assert.Equal(0UL, entry.Index); // Index 0 because [0x00]#0 sorts first
    }

    /// <summary>
    /// Multiple spend redeemers get sequential indices from sorted order.
    /// </summary>
    [Fact]
    public void MultipleSpendRedeemersGetSequentialIndices()
    {
        RedeemerBuilder builder = new();

        // Add in reverse order — [0x02]#0, [0x01]#0, [0x00]#0
        WitnessRequirements reqs2 = new() { RedeemerData = MakeRedeemer(3) };
        builder.AddSpend(new InputBuilderResult(MakeInput(2, 0), MakeOutput(), reqs2));

        WitnessRequirements reqs1 = new() { RedeemerData = MakeRedeemer(2) };
        builder.AddSpend(new InputBuilderResult(MakeInput(1, 0), MakeOutput(), reqs1));

        WitnessRequirements reqs0 = new() { RedeemerData = MakeRedeemer(1) };
        builder.AddSpend(new InputBuilderResult(MakeInput(0, 0), MakeOutput(), reqs0));

        IRedeemers redeemers = builder.Build();
        RedeemerList list = Assert.IsType<RedeemerList>(redeemers);

        Assert.Equal(3, list.Value.Count);

        // Sorted order: [0x00]#0 → index 0, [0x01]#0 → index 1, [0x02]#0 → index 2
        Assert.Equal(0UL, list.Value[0].Index);
        Assert.Equal(1UL, list.Value[1].Index);
        Assert.Equal(2UL, list.Value[2].Index);
    }

    /// <summary>
    /// Mint redeemers get indices from sorted policy ID order.
    /// </summary>
    [Fact]
    public void MintRedeemersGetSortedPolicyIndices()
    {
        RedeemerBuilder builder = new();

        // Add policies in reverse order
        builder.AddMint("CC", MakeRedeemer(2));
        builder.AddMint("AA", MakeRedeemer(1));
        builder.AddMint("BB", MakeRedeemer(3));

        IRedeemers redeemers = builder.Build();
        RedeemerList list = Assert.IsType<RedeemerList>(redeemers);

        Assert.Equal(3, list.Value.Count);

        // All should have tag 1 (Mint)
        Assert.All(list.Value, e => Assert.Equal(1, e.Tag));

        // Sorted: AA → 0, BB → 1, CC → 2
        Assert.Equal(0UL, list.Value[0].Index);
        Assert.Equal(1UL, list.Value[1].Index);
        Assert.Equal(2UL, list.Value[2].Index);
    }

    /// <summary>
    /// UpdateExUnits correctly updates a redeemer's execution units.
    /// </summary>
    [Fact]
    public void UpdateExUnitsAppliesCorrectly()
    {
        RedeemerBuilder builder = new();

        WitnessRequirements reqs = new() { RedeemerData = MakeRedeemer(1) };
        builder.AddSpend(new InputBuilderResult(MakeInput(0, 0), MakeOutput(), reqs));

        builder.UpdateExUnits(0, 0, ExUnits.Create(1000, 2000));

        IRedeemers redeemers = builder.Build();
        RedeemerList list = Assert.IsType<RedeemerList>(redeemers);

        Assert.Equal(1000UL, list.Value[0].ExUnits.Mem);
        Assert.Equal(2000UL, list.Value[0].ExUnits.Steps);
    }

    [Fact]
    public void HasRedeemersReturnsFalseWhenEmpty()
    {
        RedeemerBuilder builder = new();
        Assert.False(builder.HasRedeemers);
    }

    [Fact]
    public void HasRedeemersReturnsTrueAfterAddingSpend()
    {
        RedeemerBuilder builder = new();
        WitnessRequirements reqs = new() { RedeemerData = MakeRedeemer(1) };
        builder.AddSpend(new InputBuilderResult(MakeInput(0, 0), MakeOutput(), reqs));
        Assert.True(builder.HasRedeemers);
    }
}

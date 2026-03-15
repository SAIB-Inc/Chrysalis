using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Utils;
using Xunit;

namespace Chrysalis.Tx.Test;

public class CoinSelectionTests
{
    private static ResolvedInput MakeUtxo(byte id, ulong lovelace) =>
        new(
            TransactionInput.Create(new byte[] { id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 0),
            AlonzoTransactionOutput.Create(
                Address.Create(new byte[57]),
                Lovelace.Create(lovelace),
                null));

    [Fact]
    public void LargestFirstSelectsSufficientInputs()
    {
        List<ResolvedInput> utxos =
        [
            MakeUtxo(1, 1_000_000),
            MakeUtxo(2, 5_000_000),
            MakeUtxo(3, 3_000_000),
        ];

        CoinSelectionResult result = CoinSelectionUtil.Select(
            utxos, [Lovelace.Create(4_000_000)], CoinSelectionStrategy.LargestFirst);

        Assert.True(result.Inputs.Count >= 1);
        ulong totalSelected = 0;
        foreach (ResolvedInput input in result.Inputs)
        {
            totalSelected += input.Output.Amount().Lovelace();
        }
        Assert.True(totalSelected >= 4_000_000);
    }

    [Fact]
    public void LargestFirstThrowsOnInsufficientFunds()
    {
        List<ResolvedInput> utxos = [MakeUtxo(1, 1_000_000)];

        Assert.Throws<InvalidOperationException>(() =>
            CoinSelectionUtil.Select(utxos, [Lovelace.Create(5_000_000)], CoinSelectionStrategy.LargestFirst));
    }

    [Fact]
    public void RandomImproveSelectsSufficientInputs()
    {
        List<ResolvedInput> utxos =
        [
            MakeUtxo(1, 1_000_000),
            MakeUtxo(2, 5_000_000),
            MakeUtxo(3, 3_000_000),
            MakeUtxo(4, 2_000_000),
        ];

        CoinSelectionResult result = CoinSelectionUtil.Select(
            utxos, [Lovelace.Create(4_000_000)], CoinSelectionStrategy.RandomImprove);

        ulong totalSelected = 0;
        foreach (ResolvedInput input in result.Inputs)
        {
            totalSelected += input.Output.Amount().Lovelace();
        }
        Assert.True(totalSelected >= 4_000_000);
        Assert.Equal(totalSelected - 4_000_000, result.LovelaceChange);
    }

    [Fact]
    public void RandomImproveThrowsOnInsufficientFunds()
    {
        List<ResolvedInput> utxos = [MakeUtxo(1, 1_000_000)];

        Assert.Throws<InvalidOperationException>(() =>
            CoinSelectionUtil.Select(utxos, [Lovelace.Create(5_000_000)], CoinSelectionStrategy.RandomImprove));
    }

    [Fact]
    public void SelectRespectsMaxInputs()
    {
        List<ResolvedInput> utxos =
        [
            MakeUtxo(1, 1_000_000),
            MakeUtxo(2, 1_000_000),
            MakeUtxo(3, 1_000_000),
            MakeUtxo(4, 1_000_000),
            MakeUtxo(5, 1_000_000),
        ];

        CoinSelectionResult result = CoinSelectionUtil.Select(
            utxos, [Lovelace.Create(2_000_000)], CoinSelectionStrategy.LargestFirst, maxInputs: 2);

        Assert.True(result.Inputs.Count <= 2);
    }

    [Fact]
    public void SelectDispatchesCorrectStrategy()
    {
        List<ResolvedInput> utxos = [MakeUtxo(1, 10_000_000)];

        // Both strategies should work for a simple case
        CoinSelectionResult r1 = CoinSelectionUtil.Select(utxos, [Lovelace.Create(5_000_000)], CoinSelectionStrategy.LargestFirst);
        CoinSelectionResult r2 = CoinSelectionUtil.Select(utxos, [Lovelace.Create(5_000_000)], CoinSelectionStrategy.RandomImprove);

        Assert.Single(r1.Inputs);
        Assert.Single(r2.Inputs);
        Assert.Equal(5_000_000UL, r1.LovelaceChange);
        Assert.Equal(5_000_000UL, r2.LovelaceChange);
    }
}

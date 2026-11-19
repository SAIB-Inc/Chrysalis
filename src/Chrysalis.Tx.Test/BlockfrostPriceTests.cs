using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Test.Providers;
using Xunit;

namespace Chrysalis.Tx.Test;

public class BlockfrostPriceTests
{
    [Theory]
    [InlineData("0.00000015", 3UL, 20_000_000UL)]
    [InlineData("0.05770005", 1_154_001UL, 20_000_000UL)]
    [InlineData("0.0577", 577UL, 10_000UL)]
    [InlineData("0.0000721", 721UL, 10_000_000UL)]
    [InlineData("7.21e-05", 721UL, 10_000_000UL)]
    [InlineData("0.00000000000000000016", 1UL, 6_250_000_000_000_000_000UL)]
    [InlineData("9223372036854775807.5", ulong.MaxValue, 2UL)]
    public async Task GetParameters_PreservesDecimalLiteralPrice(string literal, ulong expectedNumerator, ulong expectedDenominator)
    {
        using StubHttpHandler handler = Price(literal);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using Blockfrost provider = new("CANARY-price-test-project", handler: handler);
        ProtocolParams parameters = await provider.GetParametersAsync(timeout.Token);
        ExUnitPrices prices = Assert.IsType<ExUnitPrices>(parameters.ExecutionCosts);
        Assert.Equal(expectedNumerator, prices.MemPrice.Numerator);
        Assert.Equal(expectedDenominator, prices.MemPrice.Denominator);
        Assert.Equal(expectedNumerator, prices.StepPrice.Numerator);
        Assert.Equal(expectedDenominator, prices.StepPrice.Denominator);
    }

    [Fact]
    public async Task RejectsARatioThatDoesNotFit()
    {
        using StubHttpHandler handler = Price("0.00000000000000000003");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using Blockfrost provider = new("CANARY-price-test-project", handler: handler);
        Assert.False((await Assert.ThrowsAsync<ProviderException>(() => provider.GetParametersAsync(timeout.Token))).IsTransient);
    }

    // Send a literal through the public provider without passing through double.
    private static StubHttpHandler Price(string literal) => new(_ => StubHttpHandler.Json($$"""
        {
            "price_mem": {{literal}},
            "price_step": {{literal}},
            "max_tx_ex_mem": "0",
            "max_tx_ex_steps": "0",
            "max_block_ex_mem": "0",
            "max_block_ex_steps": "0",
            "max_val_size": "0",
            "coins_per_utxo_size": "0",
            "min_fee_ref_script_cost_per_byte": 0
        }
        """));
}

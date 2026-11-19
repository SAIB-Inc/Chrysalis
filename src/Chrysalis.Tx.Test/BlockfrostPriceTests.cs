using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Test.Providers;
using Xunit;

namespace Chrysalis.Tx.Test;

public class BlockfrostPriceTests
{
    [Theory]
    [InlineData("0.00000015", 2UL)]
    [InlineData("0.05770005", 577000UL)]
    [InlineData("0.0577", 577000UL)]
    [InlineData("0.0000721", 721UL)]
    public async Task GetParameters_PreservesDecimalLiteralPriceRounding(string literal, ulong expectedNumerator)
    {
        // Send the literal JSON number through the public provider, without constructing a
        // double in the test or duplicating the provider's price-conversion implementation.
        string json = $$"""
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
            """;

        using StubHttpHandler handler = new(_ => StubHttpHandler.Json(json));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using Blockfrost provider = new("CANARY-price-test-project", handler: handler);
        ProtocolParams parameters = await provider.GetParametersAsync(timeout.Token);
        ExUnitPrices prices = Assert.IsType<ExUnitPrices>(parameters.ExecutionCosts);
        Assert.Equal(expectedNumerator, prices.MemPrice.Numerator);
        Assert.Equal(10_000_000UL, prices.MemPrice.Denominator);
        Assert.Equal(expectedNumerator, prices.StepPrice.Numerator);
        Assert.Equal(10_000_000UL, prices.StepPrice.Denominator);
    }
}

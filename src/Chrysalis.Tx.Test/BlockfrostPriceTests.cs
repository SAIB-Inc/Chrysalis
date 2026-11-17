using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Providers;
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

        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        IPEndPoint address = (IPEndPoint)listener.LocalEndpoint;
        string url = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{address.Port}/");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using Blockfrost provider = new("unused-test-project", url: url);
        Task<ProtocolParams> parametersTask = provider.GetParametersAsync();
        try
        {
            await ServeResponseAsync(listener, json, timeout.Token);
        }
        finally
        {
            // Complete the client request even if the server fails, before disposing its provider.
            _ = await parametersTask;
        }

        ProtocolParams parameters = await parametersTask;
        ExUnitPrices prices = Assert.IsType<ExUnitPrices>(parameters.ExecutionCosts);
        Assert.Equal(expectedNumerator, prices.MemPrice.Numerator);
        Assert.Equal(10_000_000UL, prices.MemPrice.Denominator);
        Assert.Equal(expectedNumerator, prices.StepPrice.Numerator);
        Assert.Equal(10_000_000UL, prices.StepPrice.Denominator);
    }

    private static async Task ServeResponseAsync(TcpListener listener, string json, CancellationToken cancellationToken)
    {
        using TcpClient connection = await listener.AcceptTcpClientAsync(cancellationToken);
        await using NetworkStream stream = connection.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 })
        {
            // Consume this single request's HTTP headers before returning its JSON response.
        }

        string response = string.Create(CultureInfo.InvariantCulture,
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken);
    }
}

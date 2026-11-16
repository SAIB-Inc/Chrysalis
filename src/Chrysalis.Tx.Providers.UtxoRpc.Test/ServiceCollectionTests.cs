using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Exercises deferred configuration and the shared singleton lifetime.</summary>
public sealed class ServiceCollectionTests
{
    /// <summary>The configured singleton sends its header and survives repeated disposal.</summary>
    /// <returns>A task for the registered provider.</returns>
    [Fact]
    public async Task RegistersOneInstanceWithItsConfiguredHeader()
    {
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        string? seen = null;
        fake.Handler = (_, context) =>
        {
            seen = context.RequestHeaders.GetValue("dmtr-api-key");
            return Task.FromResult(FakeUtxoRpc.Answer(new()));
        };
        IConfiguration configuration = Configuration(fake.Endpoint.ToString());
        ServiceCollection services = new();
        _ = services.AddUtxoRpcProvider(configuration);
        UtxoRpc provider;
        using (ServiceProvider container = services.BuildServiceProvider())
        {
            provider = container.GetRequiredService<UtxoRpc>();
            Assert.Same(provider, container.GetRequiredService<ICardanoDataProvider>());
            Assert.Same(provider, container.GetRequiredService<UtxoRpc>());
            _ = await provider.GetParametersAsync();
            Assert.Equal("CANARY", seen);
            Assert.Equal(NetworkType.Preprod, provider.NetworkType);
        }
        provider.Dispose();
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetParametersAsync());
    }

    /// <summary>Missing required keys throw at first resolution rather than at registration.</summary>
    /// <param name="missing">The omitted required key.</param>
    [Theory]
    [InlineData("UtxoRpcEndpoint")]
    [InlineData("NetworkType")]
    public void ReadsRequiredKeysOnlyAtFirstResolve(string missing)
    {
        Dictionary<string, string?> values = new() { ["UtxoRpcEndpoint"] = "http://127.0.0.1:1", ["NetworkType"] = "Preprod" };
        _ = values.Remove(missing);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        ServiceCollection services = new();
        Assert.Same(services, services.AddUtxoRpcProvider(configuration));
        using ServiceProvider container = services.BuildServiceProvider();
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(container.GetRequiredService<UtxoRpc>);
        Assert.Contains(missing, error.Message, StringComparison.Ordinal);
    }

    /// <summary>The existing and optional provider read the same network configuration key.</summary>
    [Fact]
    public void SharesNetworkConfigurationWithTheExistingRegistration()
    {
        IConfiguration configuration = Configuration("http://127.0.0.1:1");
        ServiceCollection existing = new();
        _ = existing.AddCardanoProvider(configuration);
        using ServiceProvider original = existing.BuildServiceProvider();
        ServiceCollection optional = new();
        _ = optional.AddUtxoRpcProvider(configuration);
        using ServiceProvider added = optional.BuildServiceProvider();
        Assert.Equal(original.GetRequiredService<ICardanoDataProvider>().NetworkType, added.GetRequiredService<UtxoRpc>().NetworkType);
    }

    /// <summary>Registration itself validates only null arguments.</summary>
    [Fact]
    public void RejectsNullRegistrationArguments()
    {
        _ = Assert.Throws<ArgumentNullException>(() => UtxoRpcServiceCollectionExtensions.AddUtxoRpcProvider(null!, Configuration("http://127.0.0.1:1")));
        _ = Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddUtxoRpcProvider(null!));
    }

    private static IConfiguration Configuration(string endpoint) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["UtxoRpcEndpoint"] = endpoint,
        ["NetworkType"] = "Preprod",
        ["UtxoRpcHeaders:dmtr-api-key"] = "CANARY",
        ["BlockfrostProjectId"] = "CANARY"
    }).Build();
}

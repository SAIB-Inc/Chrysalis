using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Extension methods for registering Cardano data providers with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds a Cardano data provider to the service collection based on configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section with provider settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCardanoProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string provider = configuration.GetValue<string>("Provider") ?? "Blockfrost";
        NetworkType networkType = configuration.GetValue<NetworkType>("NetworkType");

        return provider switch
        {
            "Blockfrost" => services.AddSingleton<ICardanoDataProvider>(_ =>
            {
                string projectId = configuration.GetValue<string>("BlockfrostProjectId")
                    ?? throw new InvalidOperationException("BlockfrostProjectId is required when using Blockfrost provider");
                return new Blockfrost(projectId, networkType);
            }),

            "Kupmios" => services.AddSingleton<ICardanoDataProvider>(_ =>
            {
                string kupoUrl = configuration.GetValue<string>("KupoUrl")
                    ?? throw new InvalidOperationException("KupoUrl is required when using Kupmios provider");
                string ogmiosUrl = configuration.GetValue<string>("OgmiosUrl")
                    ?? throw new InvalidOperationException("OgmiosUrl is required when using Kupmios provider");
                return new Kupmios(kupoUrl, ogmiosUrl, networkType);
            }),

            "Ouroboros" => services.AddSingleton<ICardanoDataProvider>(_ =>
            {
                string socketPath = configuration.GetValue<string>("OuroborosSocketPath")
                    ?? throw new InvalidOperationException("OuroborosSocketPath is required when using Ouroboros provider");
                return new Ouroboros(socketPath, Ouroboros.GetNetworkMagic(networkType));
            }),

            _ => throw new InvalidOperationException($"Unknown provider type: {provider}. Supported providers are: Blockfrost, Kupmios, Ouroboros")
        };
    }
}

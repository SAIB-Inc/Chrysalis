using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Tx.Extensions;

public static class ServiceCollectionExtensions
{
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

            "Kupo" => services.AddSingleton<ICardanoDataProvider>(_ =>
            {
                string kupoUrl = configuration.GetValue<string>("KupoUrl")
                    ?? throw new InvalidOperationException("KupoUrl is required when using Kupo provider");
                return new Kupo(kupoUrl, networkType);
            }),

            "Ouroboros" => services.AddSingleton<ICardanoDataProvider>(_ =>
            {
                string socketPath = configuration.GetValue<string>("OuroborosSocketPath")
                    ?? throw new InvalidOperationException("OuroborosSocketPath is required when using Ouroboros provider");
                return new Ouroboros(socketPath, Ouroboros.GetNetworkMagic(networkType));
            }),

            _ => throw new InvalidOperationException($"Unknown provider type: {provider}. Supported providers are: Blockfrost, Kupo, Ouroboros")
        };
    }
}
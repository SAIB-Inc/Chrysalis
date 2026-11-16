using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chrysalis.Tx.Providers;

/// <summary>Registers the optional UTxO RPC provider.</summary>
public static class UtxoRpcServiceCollectionExtensions
{
    /// <summary>Registers one provider under its concrete type and the provider interface.</summary>
    /// <param name="services">The services to extend.</param>
    /// <param name="configuration">The endpoint, network and optional headers.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddUtxoRpcProvider(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        _ = services.AddSingleton(_ =>
        {
            string endpoint = configuration.GetValue<string>("UtxoRpcEndpoint") ?? throw new InvalidOperationException("UtxoRpcEndpoint is required.");
            NetworkType network = configuration.GetValue<NetworkType?>("NetworkType") ?? throw new InvalidOperationException("NetworkType is required.");
            Dictionary<string, string>? headers = null;
            foreach (IConfigurationSection item in configuration.GetSection("UtxoRpcHeaders").GetChildren())
            {
                (headers ??= []).Add(item.Key, item.Value ?? throw new InvalidOperationException("UtxoRpcHeaders contains a missing value."));
            }

            return new UtxoRpc(new Uri(endpoint), network, headers);
        });
        return services.AddSingleton<ICardanoDataProvider>(provider => provider.GetRequiredService<UtxoRpc>());
    }
}

using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types.Cardano.Core.Common;

namespace Chrysalis.Tx.Providers;

/// <summary>Builds immutable ledger values from decoded provider amounts.</summary>
public static class ProviderValues
{
    /// <summary>Groups by policy, rejecting a repeated policy and asset-name pair.</summary>
    public static IValue Create(ulong lovelace, IEnumerable<(ReadOnlyMemory<byte> Policy, ReadOnlyMemory<byte> Name, ulong Quantity)> assets,
        string provider, string operation)
    {
        ArgumentNullException.ThrowIfNull(assets);
        Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> byPolicy = [with(ReadOnlyMemoryComparer.Instance)];
        foreach ((ReadOnlyMemory<byte> policy, ReadOnlyMemory<byte> name, ulong quantity) in assets)
        {
            if (policy.Length != 28 || name.Length > 32) { throw ProviderHttp.Unusable(provider, operation, "invalid asset identifier"); }
            if (!byPolicy.TryGetValue(policy, out Dictionary<ReadOnlyMemory<byte>, ulong>? bundle))
            {
                bundle = [with(ReadOnlyMemoryComparer.Instance)];
                byPolicy.Add(policy, bundle);
            }
            if (!bundle.TryAdd(name, quantity)) { throw ProviderHttp.Unusable(provider, operation, "repeated asset"); }
        }
        if (byPolicy.Count == 0) { return Lovelace.Create(lovelace); }
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> output = [with(ReadOnlyMemoryComparer.Instance)];
        foreach ((ReadOnlyMemory<byte> policy, Dictionary<ReadOnlyMemory<byte>, ulong> bundle) in byPolicy)
        {
            output.Add(policy, TokenBundleOutput.Create(bundle));
        }
        return LovelaceWithMultiAsset.Create(lovelace, MultiAssetOutput.Create(output));
    }
}

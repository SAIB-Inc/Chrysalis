using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types.Cardano.Core.Common;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Fluent builder for constructing <see cref="MultiAssetMint"/> values.
/// Handles all nested dictionary and comparer ceremony internally.
/// </summary>
public class MintBuilder
{
    private readonly Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, long>> _byPolicy = new(ReadOnlyMemoryComparer.Instance);

    private MintBuilder() { }

    /// <summary>
    /// Creates a new empty MintBuilder.
    /// </summary>
    public static MintBuilder Create() => new();

    /// <summary>
    /// Adds a token to mint or burn using hex-encoded identifiers.
    /// </summary>
    /// <param name="policyHex">The hex-encoded policy ID (56 characters).</param>
    /// <param name="assetNameHex">The hex-encoded asset name.</param>
    /// <param name="amount">The quantity to mint (positive) or burn (negative).</param>
    /// <returns>This builder for chaining.</returns>
    public MintBuilder AddToken(string policyHex, string assetNameHex, long amount)
    {
        return AddToken(Convert.FromHexString(policyHex), Convert.FromHexString(assetNameHex), amount);
    }

    /// <summary>
    /// Adds a token to mint or burn using raw byte arrays.
    /// </summary>
    /// <param name="policyId">The policy ID bytes.</param>
    /// <param name="assetName">The asset name bytes.</param>
    /// <param name="amount">The quantity to mint (positive) or burn (negative).</param>
    /// <returns>This builder for chaining.</returns>
    public MintBuilder AddToken(byte[] policyId, byte[] assetName, long amount)
    {
        ReadOnlyMemory<byte> policyKey = policyId;
        ReadOnlyMemory<byte> nameKey = assetName;

        if (!_byPolicy.TryGetValue(policyKey, out Dictionary<ReadOnlyMemory<byte>, long>? bundle))
        {
            bundle = new(ReadOnlyMemoryComparer.Instance);
            _byPolicy[policyKey] = bundle;
        }

        bundle[nameKey] = bundle.TryGetValue(nameKey, out long existing)
            ? existing + amount
            : amount;

        return this;
    }

    /// <summary>
    /// Builds the <see cref="MultiAssetMint"/> from the accumulated tokens.
    /// </summary>
    /// <returns>The constructed MultiAssetMint.</returns>
    public MultiAssetMint Build()
    {
        Dictionary<ReadOnlyMemory<byte>, TokenBundleMint> multiAsset = new(ReadOnlyMemoryComparer.Instance);
        foreach (KeyValuePair<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, long>> entry in _byPolicy)
        {
            multiAsset[entry.Key] = TokenBundleMint.Create(entry.Value);
        }

        return MultiAssetMint.Create(multiAsset);
    }
}

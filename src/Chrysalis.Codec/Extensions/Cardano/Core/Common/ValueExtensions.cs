using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types.Cardano.Core.Common;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Common;

/// <summary>
/// Static factory and extension methods for <see cref="IValue"/>.
/// </summary>
public static class Value
{
    /// <summary>
    /// Creates an <see cref="IValue"/> from a lovelace amount.
    /// </summary>
    /// <param name="lovelace">The lovelace amount.</param>
    /// <returns>A new lovelace-only value.</returns>
    public static IValue FromLovelace(ulong lovelace) => Lovelace.Create(lovelace);
}

/// <summary>
/// Extension methods for <see cref="IValue"/> to access lovelace and multi-asset amounts.
/// </summary>
public static class ValueExtensions
{
    /// <summary>
    /// Gets the lovelace amount from the value.
    /// </summary>
    /// <param name="self">The value instance.</param>
    /// <returns>The lovelace amount.</returns>
    public static ulong Lovelace(this IValue self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            Lovelace lovelace => lovelace.Amount,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.Amount,
            _ => default
        };
    }

    /// <summary>
    /// Gets the multi-asset dictionary from the value.
    /// </summary>
    /// <param name="self">The value instance.</param>
    /// <returns>The multi-asset dictionary mapping hex-encoded policy IDs to token bundles.</returns>
    public static Dictionary<string, TokenBundleOutput> MultiAsset(this IValue self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset.Value
                .ToDictionary(
                    kvp => Convert.ToHexString(kvp.Key.Span).ToUpperInvariant(),
                    kvp => kvp.Value
                ),
            _ => []
        };
    }

    /// <summary>
    /// Gets the quantity of a specific asset identified by raw policy ID and asset name bytes.
    /// </summary>
    /// <param name="self">The value instance.</param>
    /// <param name="policyId">The policy ID bytes.</param>
    /// <param name="assetName">The asset name bytes.</param>
    /// <returns>The quantity, or null if the asset is not present.</returns>
    public static ulong? QuantityOf(this IValue self, ReadOnlyMemory<byte> policyId, ReadOnlyMemory<byte> assetName)
    {
        ArgumentNullException.ThrowIfNull(self);

        if (self is LovelaceWithMultiAsset multiAsset)
        {
            foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> policyEntry in multiAsset.MultiAsset.Value)
            {
                if (policyEntry.Key.Span.SequenceEqual(policyId.Span))
                {
                    foreach (KeyValuePair<ReadOnlyMemory<byte>, ulong> token in policyEntry.Value.Value)
                    {
                        if (token.Key.Span.SequenceEqual(assetName.Span))
                        {
                            return token.Value;
                        }
                    }
                    return null;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the quantity of a specific asset identified by its subject (policy ID + asset name).
    /// </summary>
    /// <param name="self">The value instance.</param>
    /// <param name="subject">The subject string (hex-encoded policy ID concatenated with hex-encoded asset name).</param>
    /// <returns>The quantity, or null if the asset is not present or the value has no multi-assets.</returns>
    public static ulong? QuantityOf(this IValue self, string subject)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(subject);

        if (self is LovelaceWithMultiAsset multiAsset)
        {
            ulong amount = multiAsset.MultiAsset.ToDict()
                .SelectMany(ma =>
                    ma.Value.ToDict()
                    .Where(tb =>
                    {
                        string policyId = ma.Key;
                        string assetName = tb.Key;
                        string fullSubject = string.Concat(policyId, assetName);

                        return string.Equals(fullSubject, subject, StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(tb => tb.Value)
                )
                .FirstOrDefault();

            return amount;
        }

        return null;
    }

    /// <summary>
    /// Adds a token to the value, returning a new <see cref="LovelaceWithMultiAsset"/>.
    /// If the value is <see cref="Types.Cardano.Core.Common.Lovelace"/>, promotes it automatically.
    /// If a token with the same policy and name already exists, the quantities are added.
    /// </summary>
    /// <param name="self">The value to extend.</param>
    /// <param name="policyIdHex">The hex-encoded policy ID (56 characters).</param>
    /// <param name="assetNameHex">The hex-encoded asset name.</param>
    /// <param name="amount">The token quantity.</param>
    /// <returns>A new value containing the original lovelace plus all tokens.</returns>
    public static IValue WithToken(this IValue self, string policyIdHex, string assetNameHex, ulong amount)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(policyIdHex);
        ArgumentNullException.ThrowIfNull(assetNameHex);
        return self.WithToken(Convert.FromHexString(policyIdHex), Convert.FromHexString(assetNameHex), amount);
    }

    /// <summary>
    /// Adds a token to the value using raw byte arrays for policy ID and asset name.
    /// </summary>
    public static IValue WithToken(this IValue self, byte[] policyId, byte[] assetName, ulong amount)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(policyId);
        ArgumentNullException.ThrowIfNull(assetName);

        ulong lovelace = self.Lovelace();
        Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> byPolicy = new(ReadOnlyMemoryComparer.Instance);
        CollectTokens(self, byPolicy);

        ReadOnlyMemory<byte> policyKey = policyId;
        ReadOnlyMemory<byte> nameKey = assetName;

        if (!byPolicy.TryGetValue(policyKey, out Dictionary<ReadOnlyMemory<byte>, ulong>? bundle))
        {
            bundle = new(ReadOnlyMemoryComparer.Instance);
            byPolicy[policyKey] = bundle;
        }

        bundle[nameKey] = bundle.TryGetValue(nameKey, out ulong existingAmount)
            ? existingAmount + amount
            : amount;

        return LovelaceWithMultiAsset.Create(lovelace, BuildMultiAsset(byPolicy));
    }

    /// <summary>
    /// Flattens the value into a dictionary of asset quantities.
    /// Keys: "lovelace" for ADA, hex-encoded "policyId + assetName" for tokens.
    /// </summary>
    public static Dictionary<string, ulong> ToAssetDictionary(this IValue self)
    {
        ArgumentNullException.ThrowIfNull(self);
        Dictionary<string, ulong> result = new(StringComparer.OrdinalIgnoreCase)
        {
            ["lovelace"] = self.Lovelace()
        };

        if (self is LovelaceWithMultiAsset multiAsset)
        {
            foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> policyEntry in multiAsset.MultiAsset.Value)
            {
                string policyHex = Convert.ToHexStringLower(policyEntry.Key.Span);
                foreach (KeyValuePair<ReadOnlyMemory<byte>, ulong> token in policyEntry.Value.Value)
                {
                    string key = string.Concat(policyHex, Convert.ToHexStringLower(token.Key.Span));
                    result[key] = token.Value;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Enumerates all tokens in the value as (PolicyId, AssetName, Quantity) tuples.
    /// Does not include the lovelace amount.
    /// </summary>
    public static IEnumerable<(string PolicyId, string AssetName, ulong Quantity)> ToAssetList(this IValue self)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (self is not LovelaceWithMultiAsset multiAsset)
        {
            yield break;
        }

        foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> policyEntry in multiAsset.MultiAsset.Value)
        {
            string policyHex = Convert.ToHexStringLower(policyEntry.Key.Span);
            foreach (KeyValuePair<ReadOnlyMemory<byte>, ulong> token in policyEntry.Value.Value)
            {
                yield return (policyHex, Convert.ToHexStringLower(token.Key.Span), token.Value);
            }
        }
    }

    /// <summary>
    /// Returns a new value with the lovelace amount changed, preserving any multi-assets.
    /// </summary>
    /// <param name="self">The value instance.</param>
    /// <param name="lovelace">The new lovelace amount.</param>
    /// <returns>A new value with the adjusted lovelace.</returns>
    public static IValue WithLovelace(this IValue self, ulong lovelace)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            LovelaceWithMultiAsset multiAsset => LovelaceWithMultiAsset.Create(lovelace, multiAsset.MultiAsset),
            _ => Types.Cardano.Core.Common.Lovelace.Create(lovelace)
        };
    }

    /// <summary>
    /// Merges two values by adding lovelace and combining all tokens.
    /// If both values contain the same token, quantities are summed.
    /// </summary>
    /// <param name="self">The first value.</param>
    /// <param name="other">The value to merge in.</param>
    /// <returns>A new value containing the combined lovelace and all tokens from both values.</returns>
    public static IValue Merge(this IValue self, IValue other)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(other);

        ulong lovelace = self.Lovelace() + other.Lovelace();
        Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> byPolicy = new(ReadOnlyMemoryComparer.Instance);

        CollectTokens(self, byPolicy);
        MergeTokens(other, byPolicy);

        if (byPolicy.Count == 0)
        {
            return Types.Cardano.Core.Common.Lovelace.Create(lovelace);
        }

        return LovelaceWithMultiAsset.Create(lovelace, BuildMultiAsset(byPolicy));
    }

    /// <summary>
    /// Subtracts another value's lovelace and tokens from this value.
    /// Tokens that reach zero are removed. Returns lovelace-only if no tokens remain.
    /// </summary>
    /// <param name="self">The value to subtract from.</param>
    /// <param name="other">The value to subtract.</param>
    /// <returns>A new value with the remaining lovelace and tokens.</returns>
    public static IValue Subtract(this IValue self, IValue other)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(other);

        ulong selfLovelace = self.Lovelace();
        ulong otherLovelace = other.Lovelace();
        ulong lovelace = selfLovelace > otherLovelace ? selfLovelace - otherLovelace : 0;

        Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> byPolicy = new(ReadOnlyMemoryComparer.Instance);
        CollectTokens(self, byPolicy);

        if (other is LovelaceWithMultiAsset otherMulti)
        {
            foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> policyEntry in otherMulti.MultiAsset.Value)
            {
                if (byPolicy.TryGetValue(policyEntry.Key, out Dictionary<ReadOnlyMemory<byte>, ulong>? bundle))
                {
                    foreach (KeyValuePair<ReadOnlyMemory<byte>, ulong> token in policyEntry.Value.Value)
                    {
                        if (bundle.TryGetValue(token.Key, out ulong existing))
                        {
                            ulong remaining = existing > token.Value ? existing - token.Value : 0;
                            if (remaining > 0)
                            {
                                bundle[token.Key] = remaining;
                            }
                            else
                            {
                                _ = bundle.Remove(token.Key);
                            }
                        }
                    }

                    if (bundle.Count == 0)
                    {
                        _ = byPolicy.Remove(policyEntry.Key);
                    }
                }
            }
        }

        if (byPolicy.Count == 0)
        {
            return Types.Cardano.Core.Common.Lovelace.Create(lovelace);
        }

        return LovelaceWithMultiAsset.Create(lovelace, BuildMultiAsset(byPolicy));
    }

    private static void CollectTokens(IValue value, Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> byPolicy)
    {
        if (value is LovelaceWithMultiAsset existing)
        {
            foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> policyEntry in existing.MultiAsset.Value)
            {
                Dictionary<ReadOnlyMemory<byte>, ulong> tokens = new(ReadOnlyMemoryComparer.Instance);
                foreach (KeyValuePair<ReadOnlyMemory<byte>, ulong> token in policyEntry.Value.Value)
                {
                    tokens[token.Key] = token.Value;
                }
                byPolicy[policyEntry.Key] = tokens;
            }
        }
    }

    private static void MergeTokens(IValue value, Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> byPolicy)
    {
        if (value is LovelaceWithMultiAsset other)
        {
            foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> policyEntry in other.MultiAsset.Value)
            {
                if (!byPolicy.TryGetValue(policyEntry.Key, out Dictionary<ReadOnlyMemory<byte>, ulong>? bundle))
                {
                    bundle = new(ReadOnlyMemoryComparer.Instance);
                    byPolicy[policyEntry.Key] = bundle;
                }

                foreach (KeyValuePair<ReadOnlyMemory<byte>, ulong> token in policyEntry.Value.Value)
                {
                    bundle[token.Key] = bundle.TryGetValue(token.Key, out ulong existing)
                        ? existing + token.Value
                        : token.Value;
                }
            }
        }
    }

    private static MultiAssetOutput BuildMultiAsset(Dictionary<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> byPolicy)
    {
        Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> multiAsset = new(ReadOnlyMemoryComparer.Instance);
        foreach (KeyValuePair<ReadOnlyMemory<byte>, Dictionary<ReadOnlyMemory<byte>, ulong>> entry in byPolicy)
        {
            multiAsset[entry.Key] = TokenBundleOutput.Create(entry.Value);
        }
        return MultiAssetOutput.Create(multiAsset);
    }
}

using System.Numerics;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models.Cbor;
using Xunit;
using Xunit.Sdk;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Compares only the fields the reference provider actually reads live.</summary>
[Trait("Category", "Integration")]
[Collection("Live")]
public sealed class ParityTests
{
    private static readonly string[] ParameterKeys =
    [
        "txFeePerByte", "txFeeFixed", "maxBlockBodySize", "maxTxSize", "maxBlockHeaderSize",
        "stakeAddressDeposit", "stakePoolDeposit", "stakePoolTargetNum", "minPoolCost", "utxoCostPerByte",
        "costModels", "executionUnitPrices", "maxTxExecutionUnits", "maxBlockExecutionUnits", "maxValueSize",
        "collateralPercentage", "maxCollateralInputs", "minFeeRefScriptCostPerByte"
    ];

    /// <summary>The exact live parameter fields agree across a stable three-read window.</summary>
    /// <returns>A task for a bounded comparison.</returns>
    [LiveFact(LiveSettings.Endpoint, LiveSettings.Network, LiveSettings.BlockfrostKeyFile)]
    public async Task CurrentParametersAgree()
    {
        // Blockfrost.cs:119-122,137-144 does not read these 12 live/exactly: protocolVersion,
        // poolPledgeInfluence, monetaryExpansion, treasuryCut, both voting thresholds, committeeMinSize,
        // committeeMaxTermLength, govActionLifetime, govActionDeposit, dRepDeposit, dRepActivity.
        // poolRetireMaxEpoch is excluded by DolosGaps along with its other named server gaps.
        using UtxoRpc provider = LiveSettings.CreateProvider();
        using Blockfrost reference = await ReferenceAsync();
        await StableAsync(async () =>
        {
            Dictionary<string, object?> before = LedgerParameters.Fields(await provider.GetParametersAsync());
            Dictionary<string, object?> expected;
            try { expected = LedgerParameters.Fields(await reference.GetParametersAsync()); }
            catch (Exception) { throw new XunitException("The Blockfrost parameter read failed."); }
            Dictionary<string, object?> after = LedgerParameters.Fields(await provider.GetParametersAsync());
            foreach (string key in ParameterKeys)
            {
                bool skip = false;
                foreach ((string gap, string? part, _) in DolosGaps.Values)
                {
                    if (gap != key) { continue; }
                    if (part is null) { skip = true; }
                    else
                    {
                        _ = Assert.IsType<Dictionary<string, object?>>(expected[key]).Remove(part);
                        _ = Assert.IsType<Dictionary<string, object?>>(before[key]).Remove(part);
                        _ = Assert.IsType<Dictionary<string, object?>>(after[key]).Remove(part);
                    }
                }
                if (!skip)
                {
                    EqualValue(expected[key], before[key], key);
                    EqualValue(expected[key], after[key], key);
                }
            }
        });
    }

    /// <summary>Wallet and public recording address UTxOs agree as sets by reference.</summary>
    /// <returns>A task for the configured addresses.</returns>
    [LiveFact(LiveSettings.Endpoint, LiveSettings.Network, LiveSettings.Addresses, LiveSettings.BlockfrostKeyFile)]
    public async Task CurrentOutputsAgree()
    {
        using UtxoRpc provider = LiveSettings.CreateProvider();
        using Blockfrost reference = await ReferenceAsync();
        foreach (string address in LiveSettings.WalletAddresses())
        {
            await StableAsync(async () =>
            {
                List<ResolvedInput> before = await provider.GetUtxosAsync([address]);
                List<ResolvedInput> expected;
                try { expected = await reference.GetUtxosAsync([address]); }
                catch (Exception) { throw new XunitException("The Blockfrost output read failed."); }
                List<ResolvedInput> after = await provider.GetUtxosAsync([address]);
                EqualOutputs(expected, before);
                EqualOutputs(expected, after);
            });
        }
    }

    private static async Task<Blockfrost> ReferenceAsync()
    {
        try
        {
            return new Blockfrost((await File.ReadAllTextAsync(LiveSettings.Require(LiveSettings.BlockfrostKeyFile))).Trim(), LiveSettings.ReadNetwork());
        }
        catch (Exception) { throw new XunitException("Cannot initialize the Blockfrost reference provider."); }
    }

    private static async Task StableAsync(Func<Task> compare)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { await compare(); return; }
            catch (XunitException) when (attempt < 4) { await Task.Delay(TimeSpan.FromSeconds(20)); }
        }
    }

    private static void EqualValue(object? expected, object? actual, string field)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        if (expected is Dictionary<string, object?> fields)
        {
            Dictionary<string, object?> values = Assert.IsType<Dictionary<string, object?>>(actual);
            foreach ((string key, object? value) in fields) { EqualValue(value, values[key], field + "." + key); }
        }
        else if (expected is CborRationalNumber rational)
        {
            CborRationalNumber value = Assert.IsType<CborRationalNumber>(actual);
            Assert.True((BigInteger)rational.Numerator * value.Denominator == (BigInteger)value.Numerator * rational.Denominator, field);
        }
        else if (expected is long[] model) { Assert.Equal(model, Assert.IsType<long[]>(actual)); }
        else { Assert.True(Equals(expected, actual), $"{field}: reference={expected}, UTxO RPC={actual}."); }
    }

    internal static void EqualOutputs(List<ResolvedInput> expected, List<ResolvedInput> actual)
    {
        Dictionary<string, ITransactionOutput> found = [with(actual.Count, StringComparer.Ordinal)];
        foreach (ResolvedInput item in actual) { found.Add(Key(item), item.Output); }
        Assert.Equal(expected.Count, found.Count);
        foreach (ResolvedInput item in expected)
        {
            string key = Key(item);
            Assert.True(found.TryGetValue(key, out ITransactionOutput? output), $"Missing output {key}.");
            Assert.True(item.Output.Address().Span.SequenceEqual(output.Address().Span), $"Address differs at {key}.");
            Assert.Equal(item.Output.Amount().Lovelace(), output.Amount().Lovelace());
            Dictionary<string, TokenBundleOutput> assets = item.Output.Amount().MultiAsset();
            Assert.Equal(assets.Count, output.Amount().MultiAsset().Count);
            foreach ((string policy, TokenBundleOutput tokens) in assets)
            {
                Assert.Equal(tokens.Value.Count, output.Amount().MultiAsset()[policy].Value.Count);
                foreach ((ReadOnlyMemory<byte> name, ulong quantity) in tokens.Value)
                {
                    Assert.Equal(quantity, output.Amount().QuantityOf(Convert.FromHexString(policy), name));
                }
            }
            ReadOnlyMemory<byte>? expectedInline = item.Output.InlineDatumRaw();
            ReadOnlyMemory<byte>? actualInline = output.InlineDatumRaw();
            Assert.Equal(expectedInline.HasValue, actualInline.HasValue);
            if (expectedInline is { } datum) { Assert.True(datum.Span.SequenceEqual(actualInline!.Value.Span), $"Inline datum differs at {key}."); }
            // Datum hashes are excluded: Blockfrost.cs:244-248 drops them. Never use DatumHash().
            // Script bytes are excluded: Blockfrost.cs:238-241 reconstructs scripts through another endpoint.
            ReadOnlyMemory<byte>? expectedScript = item.Output.ScriptRef();
            ReadOnlyMemory<byte>? actualScript = output.ScriptRef();
            // Only Blockfrost's omission is exempt: its address endpoint can lose a script that
            // /txs/{hash}/utxos reports (capture README). UTxO RPC must retain every script Blockfrost reports.
            if (expectedScript is not null) { _ = Assert.NotNull(actualScript); }
            if (expectedScript is { } script && actualScript is { } actualBytes)
            {
                Assert.Equal(CborSerializer.Deserialize<IScript>(script).GetType(), CborSerializer.Deserialize<IScript>(actualBytes).GetType());
            }
        }
    }

    private static string Key(ResolvedInput item) => $"{Convert.ToHexStringLower(item.Outref.TransactionId.Span)}#{item.Outref.Index}";
}

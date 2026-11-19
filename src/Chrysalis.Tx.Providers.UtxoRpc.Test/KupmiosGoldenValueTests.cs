using System.Numerics;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Test.Providers;
using Chrysalis.Wallet.Models.Addresses;
using Xunit;
using Spec = Utxorpc.V1Beta.Cardano;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Reads independent Conway outputs through the Kupo response mapper.</summary>
public sealed class KupmiosGoldenValueTests
{
    /// <summary>Every asset in a multi-asset policy survives the public read.</summary>
    [Fact]
    public async Task PreservesEveryAssetOfUpstreamPolicy()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Spec.Block block = Spec.Block.Parser.ParseFrom(await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Golden/u5c-factory/v1beta/conway-v11.pb"), timeout.Token));
        int count = 0;
        foreach (Spec.Tx tx in block.Body.Tx)
        {
            for (int index = 0; index < tx.Outputs.Count; index++)
            {
                Spec.TxOutput output = tx.Outputs[index];
                if (!output.Assets.Any(group => group.Assets.Count >= 2)) { continue; }
                count++;
                int[] policySizes = [.. output.Assets.Select(group => group.Assets.Count).Where(size => size >= 2).Order()];
                Assert.Equal([2, 7], policySizes);
                string address = Address.FromBytes(output.Address.ToByteArray()).ToBech32();
                Dictionary<string, ulong> expected = output.Assets.SelectMany(group => group.Assets.Select(asset =>
                    KeyValuePair.Create(AssetKey(group.PolicyId.Span, asset.Name.Span), Unsigned(asset.Quantity))))
                    .ToDictionary(StringComparer.Ordinal);
                string json = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        transaction_id = Convert.ToHexString(tx.Hash.Span),
                        output_index = index,
                        address,
                        value = new { coins = Unsigned(output.Coin), assets = expected }
                    }
                });
                using StubHttpHandler stub = new(_ => StubHttpHandler.Json(json));
                using Kupmios provider = new("https://kupo.test/", "https://ogmios.test/", handler: stub);
                ResolvedInput result = Assert.Single(await provider.GetUtxosAsync([address], timeout.Token));
                Assert.Equal(Unsigned(output.Coin), result.Output.Amount().Lovelace());
                Dictionary<string, ulong> actual = result.Output.Amount().MultiAsset().SelectMany(group => group.Value.Value.Select(asset =>
                    KeyValuePair.Create(AssetKey(Convert.FromHexString(group.Key), asset.Key.Span), asset.Value))).ToDictionary(StringComparer.Ordinal);
                foreach ((string key, ulong quantity) in expected)
                {
                    Assert.True(actual.TryGetValue(key, out ulong observed), $"Missing asset {key}");
                    Assert.Equal(quantity, observed);
                }
                Assert.Equal(expected.Count, actual.Count);
            }
        }
        Assert.Equal(1, count);
    }

    private static string AssetKey(ReadOnlySpan<byte> policy, ReadOnlySpan<byte> name) => $"{Convert.ToHexString(policy)}.{Convert.ToHexString(name)}";

    private static ulong Unsigned(Spec.BigInt value) => value.BigIntCase == Spec.BigInt.BigIntOneofCase.Int
        ? checked((ulong)value.Int) : (ulong)new BigInteger(value.BigUInt.Span, isUnsigned: true, isBigEndian: true);
}

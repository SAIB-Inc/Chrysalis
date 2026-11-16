using Chrysalis.Codec.Serialization;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Enums;
using Utxorpc.V1Beta.Query;
using Xunit;
using Xunit.Abstractions;
using Spec = Utxorpc.V1Beta.Cardano;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Reads the upstream's 33 outputs through the public provider.</summary>
/// <param name="output">Receives inline-datum shape evidence.</param>
public sealed class GoldenOutputTests(ITestOutputHelper output)
{
    /// <summary>Native output bytes decode to every independently parsed output field.</summary>
    /// <returns>A task for all five golden vectors.</returns>
    [Fact]
    public async Task DecodesAllThirtyThreeUpstreamOutputs()
    {
        string[] names = ["babbage-v7", "babbage-v8", "conway-v9", "conway-v10", "conway-v11"];
        int[] counts = [2, 9, 1, 1, 20];
        int total = 0;
        int inline = 0;
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Mainnet);
        for (int vector = 0; vector < names.Length; vector++)
        {
            Spec.Block block = Spec.Block.Parser.ParseFrom(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Golden/u5c-factory/v1beta", names[vector] + ".pb")));
            int count = 0;
            foreach (Spec.Tx tx in block.Body.Tx)
            {
                for (int index = 0; index < tx.Outputs.Count; index++)
                {
                    Spec.TxOutput expected = tx.Outputs[index];
                    AnyUtxoData item = new() { NativeBytes = expected.OriginalCbor, TxoRef = new TxoRef { Hash = tx.Hash, Index = (uint)index }, Cardano = expected };
                    fake.UtxosHandler = (_, _) => Task.FromResult(new ReadUtxosResponse { Items = { item } });
                    ResolvedInput value = Assert.IsType<ResolvedInput>(await provider.GetUtxoByOutRefAsync(Convert.ToHexStringLower(tx.Hash.Span), (uint)index, CancellationToken.None));
                    ParsedOutputs.Equal(expected, value.Output);
                    Assert.True(expected.OriginalCbor.Span.SequenceEqual(CborSerializer.Serialize(value.Output)));
                    if (expected.Datum is { Payload: not null } datum)
                    {
                        inline++;
                        output.WriteLine("{0} output {1}: datum hash={2}, payload=true, original_cbor={3}; InlineDatumRaw matches original_cbor", names[vector], index, datum.Hash.Length, datum.OriginalCbor.Length);
                    }
                    count++;
                }
            }
            Assert.Equal(counts[vector], count);
            total += count;
        }
        Assert.Equal(33, total);
        Assert.Equal(3, inline);
    }
}

using Chrysalis.Wallet.Models.Enums;
using Google.Protobuf;
using Utxorpc.V1Beta.Query;
using Xunit;
using LedgerMetadata = Chrysalis.Codec.Types.Cardano.Core.Metadata;
using Spec = Utxorpc.V1Beta.Cardano;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Compares six original transactions with upstream's parsed metadata.</summary>
public sealed class GoldenMetadataTests
{
    /// <summary>All metadata values, not only their labels, agree with the independent vectors.</summary>
    /// <returns>A task for the three source blocks.</returns>
    [Fact]
    public async Task ReadsAllSixFramedTransactions()
    {
        string[] names = ["babbage-v7", "babbage-v8", "conway-v9"];
        int total = 0;
        int present = 0;
        await using FakeUtxoRpc fake = await FakeUtxoRpc.StartAsync();
        using UtxoRpc provider = new(fake.Endpoint, NetworkType.Mainnet);
        foreach (string name in names)
        {
            Spec.Block block = Spec.Block.Parser.ParseFrom(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Golden/u5c-factory/v1beta", name + ".pb")));
            List<byte[]> txs = await VectorBlocks.ReadAsync(name);
            Assert.Equal(block.Body.Tx.Count, txs.Count);
            for (int i = 0; i < txs.Count; i++)
            {
                byte[] bytes = txs[i];
                fake.TxHandler = (_, _) => Task.FromResult(new ReadTxResponse { Tx = new AnyChainTx { NativeBytes = ByteString.CopyFrom(bytes) } });
                LedgerMetadata? result = await provider.GetTransactionMetadataAsync(Convert.ToHexStringLower(block.Body.Tx[i].Hash.Span), CancellationToken.None);
                ParsedMetadata.Equal(block.Body.Tx[i].Auxiliary?.Metadata, result);
                if (result is { } metadata) { Assert.True(metadata.Value.ContainsKey(674)); present++; }
                total++;
            }
        }
        Assert.Equal(6, total);
        Assert.Equal(3, present);
    }
}

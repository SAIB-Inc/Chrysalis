using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Byron;

namespace Chrysalis.Test;

public class ByronTests
{
    private static byte[] LoadTestBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine("TestData", filename)).Trim();
        return Convert.FromHexString(hex);
    }

    [Theory]
    [InlineData("byron1.block")]
    [InlineData("byron2.block")]
    [InlineData("byron3.block")]
    [InlineData("byron4.block")]
    [InlineData("byron5.block")]
    [InlineData("byron6.block")]
    [InlineData("byron7.block")]
    [InlineData("byron8.block")]
    public void DeserializeByronMainBlock(string filename)
    {
        byte[] cborRaw = LoadTestBlock(filename);

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Assert.NotNull(blockWithEra);
        Assert.Equal(1, blockWithEra.EraNumber);

        Block block = blockWithEra.Block;
        Assert.IsType<ByronMainBlock>(block);

        ByronMainBlock byron = (ByronMainBlock)block;
        Assert.NotNull(byron.Header);
        Assert.NotNull(byron.Body);
    }

    [Fact]
    public void DeserializeByronEbbBlock()
    {
        byte[] cborRaw = LoadTestBlock("genesis.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Assert.NotNull(blockWithEra);
        Assert.Equal(0, blockWithEra.EraNumber);

        Block block = blockWithEra.Block;
        Assert.IsType<ByronEbBlock>(block);
    }

    [Theory]
    [InlineData("byron1.block")]
    [InlineData("byron2.block")]
    [InlineData("byron3.block")]
    [InlineData("byron4.block")]
    [InlineData("byron5.block")]
    [InlineData("byron6.block")]
    [InlineData("byron7.block")]
    [InlineData("byron8.block")]
    public void ByronMainBlockIsomorphicRoundTrip(string filename)
    {
        byte[] cborRaw = LoadTestBlock(filename);

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        byte[] serialized = CborSerializer.Serialize(blockWithEra);

        Assert.Equal(Convert.ToHexString(cborRaw), Convert.ToHexString(serialized));
    }

    [Fact]
    public void ByronEbbBlockIsomorphicRoundTrip()
    {
        byte[] cborRaw = LoadTestBlock("genesis.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        byte[] serialized = CborSerializer.Serialize(blockWithEra);

        Assert.Equal(Convert.ToHexString(cborRaw), Convert.ToHexString(serialized));
    }

    [Fact]
    public void ByronMainBlockHeaderFields()
    {
        byte[] cborRaw = LoadTestBlock("byron1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        ByronMainBlock byron = (ByronMainBlock)blockWithEra.Block;

        Assert.NotNull(byron.Header.PrevBlock);
        Assert.NotNull(byron.Header.ConsensusData);
        Assert.NotNull(byron.Header.ConsensusData.SlotId);
        Assert.NotNull(byron.Header.ConsensusData.PubKey);
    }

    [Fact]
    public void ByronMainBlockTransactions()
    {
        // byron2 has transactions based on Pallas test expectations
        byte[] cborRaw = LoadTestBlock("byron2.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        ByronMainBlock byron = (ByronMainBlock)blockWithEra.Block;

        Assert.NotNull(byron.Body.TxPayload);
        Assert.True(byron.Body.TxPayload.GetValue().Any());

        ByronTxPayload firstTx = byron.Body.TxPayload.GetValue().First();
        Assert.NotNull(firstTx.Transaction);
        Assert.NotNull(firstTx.Transaction.Inputs);
        Assert.NotNull(firstTx.Transaction.Outputs);
        Assert.True(firstTx.Transaction.Inputs.GetValue().Any());
        Assert.True(firstTx.Transaction.Outputs.GetValue().Any());

        ByronTxOut firstOutput = firstTx.Transaction.Outputs.GetValue().First();
        Assert.NotNull(firstOutput.Address);
        Assert.True(firstOutput.Amount > 0);
    }
}

using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Byron;

namespace Chrysalis.Codec.Test;

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
        Assert.Equal(1, blockWithEra.EraNumber);

        IBlock block = blockWithEra.Block;
        _ = Assert.IsType<ByronMainBlock>(block);

        _ = (ByronMainBlock)block;
    }

    [Fact]
    public void DeserializeByronEbbBlock()
    {
        byte[] cborRaw = LoadTestBlock("genesis.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Assert.Equal(0, blockWithEra.EraNumber);

        IBlock block = blockWithEra.Block;
        _ = Assert.IsType<ByronEbBlock>(block);
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

        Assert.False(byron.Header.PrevBlock.IsEmpty);
        Assert.False(byron.Header.ConsensusData.PublicKey.IsEmpty);
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
        Assert.NotNull(firstTx.Transaction.Inputs);
        Assert.NotNull(firstTx.Transaction.Outputs);
        Assert.True(firstTx.Transaction.Inputs.GetValue().Any());
        Assert.True(firstTx.Transaction.Outputs.GetValue().Any());

        ByronTxOut firstOutput = firstTx.Transaction.Outputs.GetValue().First();
        Assert.True(firstOutput.Amount > 0);
    }
}

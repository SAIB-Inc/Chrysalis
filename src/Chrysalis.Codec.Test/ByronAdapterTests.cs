using System.IO.Hashing;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Byron;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Byron;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Test;

public class ByronAdapterTests
{
    // Pallas test vectors
    private static readonly string[] AddressTestVectors =
    [
        "37btjrVyb4KDXBNC4haBVPCrro8AQPHwvCMp3RFhhSVWwfFmZ6wwzSK6JK1hY6wHNmtrpTf1kdbva8TCneM2YsiXT7mrzT21EacHnPpz5YyUdj64na",
        "DdzFFzCqrht7PQiAhzrn6rNNoADJieTWBt8KeK9BZdUsGyX9ooYD9NpMCTGjQoUKcHN47g8JMXhvKogsGpQHtiQ65fZwiypjrC6d3a4Q",
        "Ae2tdPwUPEZLs4HtbuNey7tK4hTKrwNwYtGqp7bDfCy2WdR3P6735W5Yfpe",
    ];

    private static byte[] LoadTestBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine("TestData", filename)).Trim();
        return Convert.FromHexString(hex);
    }

    // -- Byron Address Tests (Pallas parity) --

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RoundtripBase58(int index)
    {
        string vector = AddressTestVectors[index];
        ByronAddress addr = ByronAddressExtensions.FromBase58(vector);
        string ours = addr.ToBase58();
        Assert.Equal(vector, ours);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PayloadCrcMatches(int index)
    {
        string vector = AddressTestVectors[index];
        ByronAddress addr = ByronAddressExtensions.FromBase58(vector);

        byte[] payloadBytes = addr.Payload.GetValue();
        uint crc = BitConverter.ToUInt32(Crc32.Hash(payloadBytes));

        Assert.Equal(crc, addr.Crc);
    }

    // -- Unified API Adapter Tests --

    [Fact]
    public void TransactionBodies_ReturnsAdaptersForByronBlock()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        IEnumerable<TransactionBody> txBodies = block.TransactionBodies();

        Assert.NotEmpty(txBodies);
        Assert.All(txBodies, txBody => Assert.IsType<ByronTransactionBodyAdapter>(txBody));
    }

    [Fact]
    public void TransactionBodies_ReturnsEmptyForEbb()
    {
        byte[] cborRaw = LoadTestBlock("genesis.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        IEnumerable<TransactionBody> txBodies = block.TransactionBodies();

        Assert.Empty(txBodies);
    }

    [Fact]
    public void Inputs_ReturnsTransactionInputsForByron()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        TransactionBody firstTx = block.TransactionBodies().First();
        IEnumerable<TransactionInput> inputs = firstTx.Inputs();

        Assert.NotEmpty(inputs);
        foreach (TransactionInput input in inputs)
        {
            Assert.False(input.TransactionId.IsEmpty);
            Assert.Equal(32, input.TransactionId.Length);
        }
    }

    [Fact]
    public void Outputs_ReturnsAdaptersForByron()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        TransactionBody firstTx = block.TransactionBodies().First();
        IEnumerable<TransactionOutput> outputs = firstTx.Outputs();

        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.IsType<ByronTransactionOutputAdapter>(output));
    }

    [Fact]
    public void OutputAddress_ReturnsByronAddressBytes()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        TransactionOutput firstOutput = block.TransactionBodies().First().Outputs().First();
        ReadOnlyMemory<byte> address = firstOutput.Address();

        Assert.False(address.IsEmpty);
    }

    [Fact]
    public void OutputAmount_ReturnsLovelaceForByron()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        TransactionOutput firstOutput = block.TransactionBodies().First().Outputs().First();
        Value amount = firstOutput.Amount();

        Lovelace lovelace = Assert.IsType<Lovelace>(amount);
        Assert.True(lovelace.Value > 0);
    }

    [Fact]
    public void Fee_ReturnsZeroForByron()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        TransactionBody firstTx = block.TransactionBodies().First();
        ulong fee = firstTx.Fee();

        Assert.Equal(0UL, fee);
    }

    [Fact]
    public void Hash_ReturnsValidHashForByronTx()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        TransactionBody firstTx = block.TransactionBodies().First();
        string hash = firstTx.Hash();

        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void NullableFields_ReturnNullForByron()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        TransactionBody firstTx = block.TransactionBodies().First();

        Assert.Null(firstTx.ValidFrom());
        Assert.Null(firstTx.ValidTo());
        Assert.Null(firstTx.Certificates());
        Assert.Null(firstTx.Withdrawals());
        Assert.Null(firstTx.AuxiliaryDataHash());
        Assert.Null(firstTx.Mint());
        Assert.Null(firstTx.ScriptDataHash());
        Assert.Null(firstTx.Collateral());
        Assert.Null(firstTx.RequiredSigners());
        Assert.Null(firstTx.NetworkId());
        Assert.Null(firstTx.CollateralChange());
        Assert.Null(firstTx.TotalCollateral());
        Assert.Null(firstTx.ReferenceInputs());
    }

    [Fact]
    public void OutputNullableFields_ReturnNullForByron()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        TransactionOutput firstOutput = block.TransactionBodies().First().Outputs().First();

        Assert.Null(firstOutput.DatumHash());
        Assert.Null(firstOutput.DatumOption());
        Assert.Null(firstOutput.ScriptRef());
    }

    [Fact]
    public void ByronTxIn_ToTransactionInput_DecodesCorrectly()
    {
        byte[] cborRaw = LoadTestBlock("byron2.block");
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        ByronMainBlock byron = (ByronMainBlock)blockWithEra.Block;

        ByronTxIn firstInput = byron.Body.TxPayload.GetValue().First().Transaction.Inputs.GetValue().First();
        TransactionInput input = firstInput.ToTransactionInput();

        Assert.Equal(32, input.TransactionId.Length);
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
    public void UnifiedApi_WorksForAllByronBlocks(string filename)
    {
        byte[] cborRaw = LoadTestBlock(filename);
        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        Block block = blockWithEra.Block;

        _ = block.Slot();
        _ = block.Height();
        string hash = block.Hash();
        IEnumerable<TransactionBody> txBodies = block.TransactionBodies();

        Assert.True(hash.Length == 64);

        foreach (TransactionBody txBody in txBodies)
        {
            string txHash = txBody.Hash();
            Assert.Equal(64, txHash.Length);

            IEnumerable<TransactionInput> inputs = txBody.Inputs();
            Assert.NotEmpty(inputs);

            IEnumerable<TransactionOutput> outputs = txBody.Outputs();
            Assert.NotEmpty(outputs);

            foreach (TransactionOutput output in outputs)
            {
                ReadOnlyMemory<byte> address = output.Address();
                Assert.False(address.IsEmpty);

                Value amount = output.Amount();
                Lovelace lovelace = Assert.IsType<Lovelace>(amount);
                Assert.True(lovelace.Value > 0);
            }
        }
    }
}

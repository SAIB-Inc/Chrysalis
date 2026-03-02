using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;

namespace Chrysalis.Test;

/// <summary>
/// Tests for Shelley, Allegra, and Mary era blocks adopted from Pallas test fixtures.
/// All blocks are in era-tagged format [era_number, block_body] sourced from
/// https://github.com/txpipe/pallas/tree/main/test_data.
/// </summary>
public class EraTests
{
    private static byte[] LoadTestBlock(string filename)
    {
        string hex = File.ReadAllText(Path.Combine("TestData", filename)).Trim();
        return Convert.FromHexString(hex);
    }

    // ─── Deserialization ────────────────────────────────────────────────────────

    [Fact]
    public void DeserializeShelleyBlock()
    {
        byte[] cborRaw = LoadTestBlock("shelley1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);

        Assert.NotNull(blockWithEra);
        Assert.Equal(2, blockWithEra.EraNumber);
        _ = Assert.IsType<AlonzoCompatibleBlock>(blockWithEra.Block);
    }

    [Fact]
    public void DeserializeAllegraBlock()
    {
        byte[] cborRaw = LoadTestBlock("allegra1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);

        Assert.NotNull(blockWithEra);
        Assert.Equal(3, blockWithEra.EraNumber);
        _ = Assert.IsType<AlonzoCompatibleBlock>(blockWithEra.Block);
    }

    [Fact]
    public void DeserializeMaryBlock()
    {
        byte[] cborRaw = LoadTestBlock("mary1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);

        Assert.NotNull(blockWithEra);
        Assert.Equal(4, blockWithEra.EraNumber);
        _ = Assert.IsType<AlonzoCompatibleBlock>(blockWithEra.Block);
    }

    // ─── Isomorphic round-trips ─────────────────────────────────────────────────

    [Fact]
    public void ShelleyBlockIsomorphicRoundTrip()
    {
        byte[] cborRaw = LoadTestBlock("shelley1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        byte[] serialized = CborSerializer.Serialize(blockWithEra);

        Assert.Equal(Convert.ToHexString(cborRaw), Convert.ToHexString(serialized));
    }

    [Fact]
    public void AllegraBlockIsomorphicRoundTrip()
    {
        byte[] cborRaw = LoadTestBlock("allegra1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        byte[] serialized = CborSerializer.Serialize(blockWithEra);

        Assert.Equal(Convert.ToHexString(cborRaw), Convert.ToHexString(serialized));
    }

    [Fact]
    public void MaryBlockIsomorphicRoundTrip()
    {
        byte[] cborRaw = LoadTestBlock("mary1.block");

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(cborRaw);
        byte[] serialized = CborSerializer.Serialize(blockWithEra);

        Assert.Equal(Convert.ToHexString(cborRaw), Convert.ToHexString(serialized));
    }
}

using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

public class HeaderBodyUnionTests
{
    private const string Union = "Chrysalis.Codec.Types.Cardano.Core.Header.IBlockHeaderBody";

    [Theory]
    [InlineData("shelley1.block", true)]
    [InlineData("allegra1.block", true)]
    [InlineData("mary1.block", true)]
    [InlineData("alonzo.block", true)]
    [InlineData("babbage.block", false)]
    [InlineData("conway.block", false)]
    [InlineData("preprod-conway.block.cbor", false)]
    public void BothHeaderBodyCasesDecodeThroughTheUnion(string file, bool alonzo)
    {
        (byte[] header, byte[] body) = HeaderParts(file);
        CborReader fields = new(body);
        fields.ReadBeginArray();
        Assert.Equal(alonzo ? 15 : 10, fields.ReadSize());
        ulong number = fields.ReadUInt64();
        ulong slot = fields.ReadUInt64();
        byte[]? previous = fields.Buffer[0] == 0xF6 ? null : fields.ReadByteString().ToArray();

        IBlockHeaderBody decoded = IBlockHeaderBody.Read(body, out int consumed);
        Assert.IsType(alonzo ? typeof(AlonzoHeaderBody) : typeof(BabbageHeaderBody), decoded);
        Assert.Equal(body.Length, consumed);
        Assert.Equal(body, decoded.Raw.ToArray());
        Assert.Equal((number, slot), (decoded.BlockNumber(), decoded.Slot()));
        Assert.Equal(previous, decoded.PrevHash()?.ToArray());
        IBlockHeaderBody viaHeader = CborSerializer.Deserialize<BlockHeader>(header).HeaderBody;
        Assert.IsType(decoded.GetType(), viaHeader);
        Assert.Equal(body, viaHeader.Raw.ToArray());
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(TestData("unknown-era-manifest.json")));
        foreach (JsonElement golden in manifest.RootElement.EnumerateArray().Where(row => row.GetProperty("path").GetString() == file && row.TryGetProperty("slot", out _)))
        {
            Assert.Equal(golden.GetProperty("slot").GetUInt64(), slot);
        }
    }

    [Theory]
    [InlineData("shelley1.block", -1)]
    [InlineData("shelley1.block", 1)]
    [InlineData("preprod-conway.block.cbor", -1)]
    [InlineData("preprod-conway.block.cbor", 1)]
    public void WrongLengthHeaderBodyFailsAtDecode(string file, int change)
    {
        List<byte[]> items = Items(HeaderParts(file).Body);
        AssertRefused(Definite(change < 0 ? items.Take(items.Count - 1) : [.. items, items[^1]]));
    }

    [Theory]
    [InlineData("80")]
    [InlineData("8A")]
    [InlineData("8F")]
    [InlineData("A0")]
    [InlineData("")]
    public void NonHeaderBodyInputFailsAtDecode(string hex) => AssertRefused(Convert.FromHexString(hex));

    [Fact]
    public void FullLengthHeaderBodyCutShortFailsAtDecode() => AssertRefused(HeaderParts("preprod-conway.block.cbor").Body[..^1]);

    [Theory]
    [InlineData("shelley1.block", true)]
    [InlineData("preprod-conway.block.cbor", false)]
    public void IndefiniteHeaderBodyStillDecodes(string file, bool alonzo)
    {
        byte[] body = [0x9F, .. Items(HeaderParts(file).Body).SelectMany(item => item), 0xFF];
        IBlockHeaderBody decoded = IBlockHeaderBody.Read(body, out int consumed);
        Assert.IsType(alonzo ? typeof(AlonzoHeaderBody) : typeof(BabbageHeaderBody), decoded);
        Assert.Equal(body.Length, consumed);
        Assert.True(decoded.IsIndefinite);
    }

    [Fact]
    public void FullLengthHeaderBodyIsReadByLengthAndBadFieldsFailWhenRead()
    {
        List<byte[]> items = Items(HeaderParts("preprod-conway.block.cbor").Body);
        ulong slot = new CborReader(items[1]).ReadUInt64();
        items[0] = Convert.FromHexString("6161");
        BabbageHeaderBody body = Assert.IsType<BabbageHeaderBody>(IBlockHeaderBody.Read(Definite(items), out _));
        Assert.Equal(slot, body.Slot);
        _ = Assert.Throws<CborDecodeException>(() => body.BlockNumber);
    }

    private static void AssertRefused(byte[] data) =>
        Assert.StartsWith(Union, Assert.Throws<CborDecodeException>(() => IBlockHeaderBody.Read(data, out _)).TypePath, StringComparison.Ordinal);

    private static string TestData(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    private static (byte[] Header, byte[] Body) HeaderParts(string file)
    {
        byte[] bytes = Path.GetExtension(file) == ".cbor"
            ? File.ReadAllBytes(TestData(file))
            : Convert.FromHexString(File.ReadAllText(TestData(file)).Trim());
        CborReader reader = new(bytes);
        reader.ReadBeginArray();
        _ = reader.ReadSize();
        if (reader.GetCurrentDataItemType() is not CborDataItemType.Array)
        {
            _ = reader.ReadUInt64();
            reader.ReadBeginArray();
            _ = reader.ReadSize();
        }
        byte[] header = reader.ReadDataItem().ToArray();
        CborReader parts = new(header);
        parts.ReadBeginArray();
        _ = parts.ReadSize();
        return (header, parts.ReadDataItem().ToArray());
    }

    private static List<byte[]> Items(byte[] body)
    {
        CborReader reader = new(body);
        reader.ReadBeginArray();
        int count = reader.ReadSize();
        List<byte[]> items = [];
        for (int i = 0; i < count; i++)
        {
            items.Add(reader.ReadDataItem().ToArray());
        }
        return items;
    }

    private static byte[] Definite(IEnumerable<byte[]> items)
    {
        List<byte[]> list = [.. items];
        return [(byte)(0x80 + list.Count), .. list.SelectMany(item => item)];
    }
}

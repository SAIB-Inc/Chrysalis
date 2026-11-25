using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Network.Cbor.ChainSync;
using SAIB.Cbor;
using Xunit;

namespace Chrysalis.Network.Tests;

public class ChainSyncHeaderTests
{
    // Synthetic structural fixtures, not ledger-valid blocks: variants 1–4 use
    // the Alonzo body layout, 5–6 Babbage; unknown variant 7 reuses the latter.
    // Slot 2 is deliberately overlong (1802);
    // definite/indefinite outer lists must retain their original bytes, not re-encode.
    // Expected digests: Python 3.12 hashlib.blake2b(bytes.fromhex(header), digest_size=32).
    // A trailing 00 documents today's accepted suffix behavior (phase-2 validation lead).
    [Theory]
    [InlineData(1, "828f011802f64040824040824040004040000040000040", "34b680037b42801aef3a6de89f29c8298a0cf9357e1e6f78541ec3f6d9867098", 23)]
    [InlineData(1, "828f011802f6404082404082404000404000004000004000", "34b680037b42801aef3a6de89f29c8298a0cf9357e1e6f78541ec3f6d9867098", 23)]
    [InlineData(1, "9f8f011802f64040824040824040004040000040000040ff", "02daa62b801afeb14c738f5775ac6c27bcde9676a7289694fba577e00e6634c8", 24)]
    [InlineData(1, "9f8f011802f64040824040824040004040000040000040ff00", "02daa62b801afeb14c738f5775ac6c27bcde9676a7289694fba577e00e6634c8", 24)]
    [InlineData(2, "828f011802f64040824040824040004040000040000040", "34b680037b42801aef3a6de89f29c8298a0cf9357e1e6f78541ec3f6d9867098", 23)]
    [InlineData(2, "828f011802f6404082404082404000404000004000004000", "34b680037b42801aef3a6de89f29c8298a0cf9357e1e6f78541ec3f6d9867098", 23)]
    [InlineData(2, "9f8f011802f64040824040824040004040000040000040ff", "02daa62b801afeb14c738f5775ac6c27bcde9676a7289694fba577e00e6634c8", 24)]
    [InlineData(2, "9f8f011802f64040824040824040004040000040000040ff00", "02daa62b801afeb14c738f5775ac6c27bcde9676a7289694fba577e00e6634c8", 24)]
    [InlineData(3, "828f011802f64040824040824040004040000040000040", "34b680037b42801aef3a6de89f29c8298a0cf9357e1e6f78541ec3f6d9867098", 23)]
    [InlineData(3, "828f011802f6404082404082404000404000004000004000", "34b680037b42801aef3a6de89f29c8298a0cf9357e1e6f78541ec3f6d9867098", 23)]
    [InlineData(3, "9f8f011802f64040824040824040004040000040000040ff", "02daa62b801afeb14c738f5775ac6c27bcde9676a7289694fba577e00e6634c8", 24)]
    [InlineData(3, "9f8f011802f64040824040824040004040000040000040ff00", "02daa62b801afeb14c738f5775ac6c27bcde9676a7289694fba577e00e6634c8", 24)]
    [InlineData(4, "828f011802f64040824040824040004040000040000040", "34b680037b42801aef3a6de89f29c8298a0cf9357e1e6f78541ec3f6d9867098", 23)]
    [InlineData(4, "828f011802f6404082404082404000404000004000004000", "34b680037b42801aef3a6de89f29c8298a0cf9357e1e6f78541ec3f6d9867098", 23)]
    [InlineData(4, "9f8f011802f64040824040824040004040000040000040ff", "02daa62b801afeb14c738f5775ac6c27bcde9676a7289694fba577e00e6634c8", 24)]
    [InlineData(4, "9f8f011802f64040824040824040004040000040000040ff00", "02daa62b801afeb14c738f5775ac6c27bcde9676a7289694fba577e00e6634c8", 24)]
    [InlineData(5, "828a011802f640408240400040844000004082000040", "7c720f268fd4956a938f70143b416ece2f2c0a5e63fff46b56033d95988aa09d", 22)]
    [InlineData(5, "828a011802f64040824040004084400000408200004000", "7c720f268fd4956a938f70143b416ece2f2c0a5e63fff46b56033d95988aa09d", 22)]
    [InlineData(5, "9f8a011802f640408240400040844000004082000040ff", "04d2fc5217c156cada82b3f259e9bac49ece9ef4b1c7771e69d15cce03218624", 23)]
    [InlineData(5, "9f8a011802f640408240400040844000004082000040ff00", "04d2fc5217c156cada82b3f259e9bac49ece9ef4b1c7771e69d15cce03218624", 23)]
    [InlineData(6, "828a011802f640408240400040844000004082000040", "7c720f268fd4956a938f70143b416ece2f2c0a5e63fff46b56033d95988aa09d", 22)]
    [InlineData(6, "828a011802f64040824040004084400000408200004000", "7c720f268fd4956a938f70143b416ece2f2c0a5e63fff46b56033d95988aa09d", 22)]
    [InlineData(6, "9f8a011802f640408240400040844000004082000040ff", "04d2fc5217c156cada82b3f259e9bac49ece9ef4b1c7771e69d15cce03218624", 23)]
    [InlineData(6, "9f8a011802f640408240400040844000004082000040ff00", "04d2fc5217c156cada82b3f259e9bac49ece9ef4b1c7771e69d15cce03218624", 23)]
    [InlineData(7, "828a011802f64040824040004084400000408200004000", "7c720f268fd4956a938f70143b416ece2f2c0a5e63fff46b56033d95988aa09d", 22)]
    public void ExtractPointHashesExactlyTheOriginalHeaderInEveryShelleyEra(int variant, string hex, string expectedHash, int headerLength)
    {
        byte[] bytes = Convert.FromHexString(hex);
        BlockHeader decoded = CborSerializer.Deserialize<BlockHeader>(bytes);
        Assert.Equal(headerLength, decoded.Raw.Length);
        Assert.Equal(bytes.AsSpan(0, headerLength).ToArray(), decoded.Raw.ToArray());
        Assert.Equal(expectedHash, decoded.Hash());
        ChainPoint point = new ChainSyncHeader(checked((byte)variant), null, bytes).ExtractPoint();
        Assert.Equal(2UL, point.Slot);
        Assert.Equal(1UL, point.BlockNumber);
        Assert.Equal(expectedHash, Convert.ToHexStringLower(point.Hash));
    }

    // Synthetic structural headers, not ledger-valid signed blocks. Independent
    // Python cbor2/hashlib goldens (including exact 8200/8201 hash prefixes and
    // decoded slot/height facts) are preserved in HDR1/review2-fix evidence.
    [Fact]
    public void ExtractPointPinsByronEbbAndMainAgainstIndependentDigests()
    {
        byte[] ebbHeader = Convert.FromHexString("85182a411141228203811903e780");
        ChainPoint ebb = new ChainSyncHeader(0, 0, ebbHeader).ExtractPoint();
        Assert.Equal(64800UL, ebb.Slot);
        Assert.Equal(0UL, ebb.BlockNumber);
        Assert.Equal("cec23e76b77bea5f33d1bf34f654c96ddbc0a85f5d1ef2353520a7abe4263fbc", Convert.ToHexStringLower(ebb.Hash));

        byte[] mainHeader = Convert.FromHexString("85182a41338483004040804040848204187b41448119ddd5820040848300000082647465737401a040");
        ChainPoint main = new ChainSyncHeader(0, 1, mainHeader).ExtractPoint();
        Assert.Equal(86523UL, main.Slot);
        Assert.Equal(56789UL, main.BlockNumber);
        Assert.Equal("90a62abc5aa2541ff647f3bea0923e7125c820a6e68d841a798ca913eabe8df9", Convert.ToHexStringLower(main.Hash));
    }

    [Theory]
    [InlineData(6, null, "", 3)]
    [InlineData(6, null, "d818", 3)]
    [InlineData(6, null, "c7d818", 3)]
    [InlineData(6, null, "d818", 0)]
    [InlineData(6, null, "d818", 24)]
    [InlineData(6, null, "d818", 256)]
    [InlineData(0, 0, "", 3)]
    [InlineData(0, 1, "d818", 3)]
    [InlineData(0, 1, "c2d818", 3)]
    [InlineData(0, 0, "d818", 0)]
    [InlineData(0, 1, "d818", 24)]
    [InlineData(0, 1, "d818", 256)]
    public void DecodeBorrowsExactSliceForDefiniteHeaders(int variant, int? byronSubTag, string tagHex, int length)
    {
        byte expectedVariant = checked((byte)variant);
        byte? expectedSubTag = byronSubTag is int sub ? checked((byte)sub) : null;
        byte[] prefix = HeaderPrefix(expectedVariant, expectedSubTag);
        byte[] tag = Convert.FromHexString(tagHex);
        byte[] size = length < 24 ? [(byte)(0x40 + length)] : length <= 0xFF ? [0x58, (byte)length] : [0x59, (byte)(length >> 8), (byte)length];
        byte[] body = [.. Enumerable.Range(0, length).Select(static i => (byte)((i * 31) + 7))];
        byte[] backing = [0xEE, 0xDD, 0xCC, .. prefix, .. tag, .. size, .. body, 0xFE, 0xED];
        ReadOnlyMemory<byte> payload = backing.AsMemory(3);
        int expectedStart = prefix.Length + tag.Length + size.Length;

        ChainSyncHeader decoded = ChainSyncHeader.Decode(payload);
        Assert.Equal(expectedVariant, decoded.Variant);
        Assert.Equal(expectedSubTag, decoded.ByronSubTag);
        Assert.Equal(body, decoded.HeaderCbor.ToArray());
        Assert.True(decoded.HeaderCbor.Equals(payload.Slice(expectedStart, length)));
    }

    [Theory]
    [InlineData(6, null, "d818", "5f4042a1b24043c3d4e541f6ff", "a1b2c3d4e5f6")]
    [InlineData(6, null, "", "5f43010203ff", "010203")]
    [InlineData(0, 1, "c2d818", "5f4041aa4043bbccddff", "aabbccdd")]
    [InlineData(0, 0, "d818", "5f40ff", "")]
    public void DecodeOwnsJoinedCopyForIndefiniteChunkedHeaders(int variant, int? byronSubTag, string tagHex, string chunksHex, string expectedHex)
    {
        byte expectedVariant = checked((byte)variant);
        byte? expectedSubTag = byronSubTag is int sub ? checked((byte)sub) : null;
        byte[] backing = [0xEE, 0xDD, 0xCC, .. HeaderPrefix(expectedVariant, expectedSubTag), .. Convert.FromHexString(tagHex + chunksHex), 0xFE, 0xED];
        byte[] expected = Convert.FromHexString(expectedHex);

        ChainSyncHeader decoded = ChainSyncHeader.Decode(backing.AsMemory(3));
        Assert.Equal(expectedVariant, decoded.Variant);
        Assert.Equal(expectedSubTag, decoded.ByronSubTag);
        Assert.Equal(expected, decoded.HeaderCbor.ToArray());
        backing.AsSpan().Fill(0x55);
        Assert.Equal(expected, decoded.HeaderCbor.ToArray());
    }

    [Theory]
    [InlineData("8206d8185f41016161ff")]
    [InlineData("820082820100d8185f41016161ff")]
    [InlineData("82065f5f4101ffff")]
    [InlineData("8206d8185f4101")]
    [InlineData("820082820100d8185f4101")]
    [InlineData("8206d81858")]
    [InlineData("8200828201005901")]
    [InlineData("8206d818440102")]
    [InlineData("8200828201005f430102")]
    [InlineData("8206d8")]
    [InlineData("820082820100d818d8")]
    [InlineData("8206d818d818d8184101")]
    [InlineData("820082820100d818d818d8184101")]
    public void DecodeRejectsMalformedHeadersWithCborException(string hex)
    {
        byte[] wire = Convert.FromHexString(hex);
        byte[] backing = [0xEE, 0xDD, .. wire];
        _ = Assert.Throws<CborException>(() => ChainSyncHeader.Decode(backing.AsMemory(2, wire.Length)));
    }

    private static byte[] HeaderPrefix(byte variant, byte? byronSubTag) =>
        variant == 0 ? [0x82, 0x00, 0x82, 0x82, byronSubTag!.Value, 0x07] : [0x82, variant];
}

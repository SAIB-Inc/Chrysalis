using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.Test.Addresses;

public class AddressPointerTests
{
    private static readonly byte[] TestPaymentHash = Convert.FromHexString("0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c");

    // Test-only: canonical VLQ pointer boundary vectors plus Cardano Ledger 95b369053fd32422695a81fb7d5289d6dd35eee2
    // (Apache-2.0; licence and notice in src/Chrysalis.Codec.Test/TestData/X7Attribution/{LICENSE,NOTICE-cardano-ledger})
    // Golden/Address.hs:124-126 (128, 2, 3) -> 81000203.
    [Theory]
    [InlineData(0UL, 0UL, 0UL, "000000")]
    [InlineData(127UL, 0UL, 0UL, "7f0000")]
    [InlineData(128UL, 0UL, 0UL, "81000000")]
    [InlineData(128UL, 2UL, 3UL, "81000203")]
    [InlineData(16383UL, 16384UL, 0UL, "ff7f81800000")]
    [InlineData(1000000UL, 42UL, 7UL, "bd84402a07")]
    [InlineData(uint.MaxValue, ushort.MaxValue, 0UL, "8fffffff7f83ff7f00")]
    [InlineData(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, "81ffffffffffffffff7f81ffffffffffffffff7f81ffffffffffffffff7f")]
    public void FromBytesAndToBytes_CanonicalVectorsAndBoundaries_RoundTrip(ulong slot, ulong txIndex, ulong certIndex, string expectedHex)
    {
        AddressPointer pointer = new(slot, txIndex, certIndex);
        byte[] expected = Convert.FromHexString(expectedHex);

        byte[] encoded = pointer.ToBytes();
        Assert.Equal(expected, encoded);

        byte[] dirtyBuffer = new byte[expected.Length + 4];
        dirtyBuffer.AsSpan().Fill(0xFF);
        Assert.True(pointer.TryWriteBytes(dirtyBuffer, out int written));
        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, dirtyBuffer[..written]);
        Assert.All(dirtyBuffer[written..], b => Assert.Equal((byte)0xFF, b));

        Assert.Equal(pointer, AddressPointer.FromBytes(expected));
        Assert.True(AddressPointer.TryFromBytes(expected, out AddressPointer decoded));
        Assert.Equal(pointer, decoded);

        Address address = Address.FromPointer(NetworkType.Mainnet, TestPaymentHash, pointer);
        Address reparsed = Address.FromBytes(address.ToBytes());
        Assert.Equal(pointer, reparsed.GetPointer());
    }

    // Test-only: includes strict rejection encodings from Pallas v1.5.0-rc.2 daecef59a1979d89515524d9583cc28655af182c
    // (Apache-2.0; licence in src/Chrysalis.Codec.Test/TestData/LICENSE-pallas),
    // pallas-addresses/src/varuint.rs:124-167 (sha256 9c04e0789f5864ed0511756187412a60d195265f40d90242d629c196a5172152)
    // and pallas-addresses/src/lib.rs:1114-1144 (sha256 0f94f662a7f58d3ce57900bb4d4a3cf6b65e46eef6c1898113dd94234a4f33f5).
    [Theory]
    [InlineData("Empty", "")]
    [InlineData("OnlyTwoCoords", "0000")]
    [InlineData("EofInFirstCoord", "81")]
    [InlineData("EofInThirdCoord", "000081")]
    [InlineData("NonCanonLeading80Single", "80000000")]
    [InlineData("NonCanonLeading80AllThree_Pallas", "800080008000")]
    [InlineData("Overflow2Pow64", "828080808080808080000000")]
    [InlineData("Overflow11Byte_Pallas", "82808080808080808080000000")]
    [InlineData("TrailingByte_Pallas", "00000000")]
    public void TryFromBytes_InvalidOrNonCanonicalEncodings_Rejected(string name, string hex)
    {
        _ = name;
        byte[] raw = Convert.FromHexString(hex);
        Assert.False(AddressPointer.TryFromBytes(raw, out AddressPointer result));
        Assert.Equal(default, result);
        _ = Assert.Throws<ArgumentException>("bytes", () => AddressPointer.FromBytes(raw));

        byte[] addrBytes = [0x41, .. TestPaymentHash, .. raw];
        _ = Assert.Throws<ArgumentException>("addressBytes", () => Address.FromBytes(addrBytes));
        _ = Assert.Throws<FormatException>(() => Address.FromBech32(Bech32Util.Encode(addrBytes, "addr")));
    }

    [Fact]
    public void TryWriteBytes_TooSmallBuffer_ReturnsFalseAndLeavesBufferUnmodified()
    {
        AddressPointer pointer = new(1000000UL, 42UL, 7UL); // 5 bytes

        Assert.False(pointer.TryWriteBytes([], out int zeroWritten));
        Assert.Equal(0, zeroWritten);

        byte[] shortBuf = [0xAA, 0xAA, 0xAA, 0xAA];
        Assert.False(pointer.TryWriteBytes(shortBuf, out int written));
        Assert.Equal(0, written);
        Assert.All(shortBuf, b => Assert.Equal((byte)0xAA, b));
    }
}

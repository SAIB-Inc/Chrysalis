using System.Reflection;
using System.Buffers.Binary;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public class CborFrameScannerTests
{
    private delegate int Scan(ReadOnlySpan<byte> bytes);

    // A positive length is Complete(length), zero NeedMore, and -1 Invalid.
    private static Scan Bind()
    {
        Type? scanner = typeof(ChannelBuffer).Assembly.GetType("Chrysalis.Network.Multiplexer.CborFrameScanner");
        Assert.NotNull(scanner);
        return scanner.GetMethod("GetFrameLength", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Scan>();
    }

    [Theory]
    [InlineData("00", 1)]
    [InlineData("40", 1)]
    [InlineData("60", 1)]
    [InlineData("80", 1)]
    [InlineData("A0", 1)]
    [InlineData("A201020304", 5)]
    [InlineData("D818D86440", 5)]
    [InlineData("5A0000000100", 6)]
    [InlineData("5B000000000000000100", 10)]
    [InlineData("9800", 2)]
    [InlineData("B800", 2)]
    [InlineData("BF9F01FF02FF", 6)]
    [InlineData("F8FF", 2)]
    [InlineData("1BFFFFFFFFFFFFFFFF", 9)]
    [InlineData("3BFFFFFFFFFFFFFFFF", 9)]
    [InlineData("D86482010200", 5)]
    [InlineData("8204D81843010203FF", 8)]
    [InlineData("9F01820203FF00", 6)]
    [InlineData("BF0161610202FF", 7)]
    [InlineData("5F4101420203FF", 7)]
    [InlineData("7F6161626263FF", 7)]
    [InlineData("F820", 2)]
    [InlineData("F93C00", 3)]
    [InlineData("FA3F800000", 5)]
    [InlineData("FB3FF0000000000000", 9)]
    public void ReturnsExactlyTheFirstItemLength(string hex, int length)
    {
        Scan scan = Bind();
        byte[] bytes = Convert.FromHexString(hex);
        Assert.Equal(length, scan(bytes));
        for (int prefix = 0; prefix < length; prefix++) { Assert.Equal(0, scan(bytes.AsSpan(0, prefix))); }
    }

    [Theory]
    [InlineData("1C")]
    [InlineData("1F")]
    [InlineData("3F")]
    [InlineData("DF")]
    [InlineData("FC")]
    [InlineData("FF")]
    [InlineData("F800")]
    [InlineData("5A80000000")]
    [InlineData("5BFFFFFFFFFFFFFFFF")]
    [InlineData("9A80000000")]
    [InlineData("BBFFFFFFFFFFFFFFFF")]
    [InlineData("815B7FFFFFFFFFFFFFFF")]
    [InlineData("81FF")]
    [InlineData("BF00FF")]
    [InlineData("5F6161FF")]
    [InlineData("7F4100FF")]
    [InlineData("5F5FFFFF")]
    public void RejectsMalformedAndOverlongDeclarationsWithoutThrowing(string hex) => Assert.Equal(-1, Bind()(Convert.FromHexString(hex)));

    [Fact]
    public void RejectsTheReviewersImpossibleTwoItemFrame() => Assert.Equal(-1, Bind()(Convert.FromHexString("825A7FFFFFF9")));

    [Theory]
    [InlineData("", 0x5a, 5, 1)]
    [InlineData("82", 0x5a, 7, 1)]
    [InlineData("8282", 0x5a, 9, 1)]
    [InlineData("A2", 0x5a, 9, 1)]
    [InlineData("9F", 0x5a, 7, 1)]
    [InlineData("BF", 0x5a, 8, 1)]
    [InlineData("BF00", 0x5a, 8, 1)]
    [InlineData("9F82", 0x5a, 9, 1)]
    [InlineData("D81882", 0x5a, 9, 1)]
    [InlineData("82815F", 0x5a, 10, 1)]
    [InlineData("7F", 0x7a, 7, 1)]
    [InlineData("82", 0x9a, 7, 1)]
    [InlineData("82", 0xba, 7, 2)]
    public void MinimumFrameSizeIncludesAllPendingSiblingsAndParentBreaks(string prefix, byte initial, int fixedBytes, int bytesPerChild)
    {
        Scan scan = Bind();
        uint largestRepresentable = (uint)((Array.MaxLength - fixedBytes) / bytesPerChild);
        // Headers alone are enough to distinguish a possible partial frame from one which can
        // never fit; the test never allocates the advertised multi-gigabyte bodies or children.
        Assert.Equal(0, scan(LengthPrefix(prefix, initial, largestRepresentable)));
        Assert.Equal(-1, scan(LengthPrefix(prefix, initial, largestRepresentable + 1)));
    }

    [Theory]
    [InlineData(0x5a)]
    [InlineData(0xc0)]
    [InlineData(0x9f)]
    public void MinimumFrameSizeIncludesPartialHeadersAndNewChildOrBreakBytes(byte initial)
    {
        Scan scan = Bind();
        byte[] arrayAtLimit = LengthPrefix("", 0x9a, (uint)(Array.MaxLength - 5));
        Assert.Equal(0, scan(arrayAtLimit));
        // Every sibling already reserves its minimum one byte. A first item that needs more
        // header bytes, a tagged child, or an indefinite break makes that minimum impossible.
        Assert.Equal(-1, scan([.. arrayAtLimit, initial]));
    }

    private static byte[] LengthPrefix(string prefix, byte initial, uint value)
    {
        byte[] bytes = [.. Convert.FromHexString(prefix), initial, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(bytes.Length - 4), value);
        return bytes;
    }

    [Fact]
    public void DeepIndefiniteItemsUseBoundedTraversalRatherThanTheCallStack()
    {
        Scan scan = Bind();
        // Ten thousand levels did not distinguish recursion; the isolated baseline probe uses
        // this same million-level shape and records stack overflow or timeout outside the suite.
        const int depth = 1_000_000;
        byte[] valid = new byte[(2 * depth) + 1];
        valid.AsSpan(0, depth).Fill(0x9f);
        valid.AsSpan(depth + 1).Fill(0xff);
        Assert.Equal(valid.Length, scan(valid));
        Assert.Equal(0, scan(valid.AsSpan(0, depth)));
        valid[depth] = 0x1c;
        Assert.Equal(-1, scan(valid));
    }

    // Allocation/pool-return guard only; the recursion-discriminating depth is the million-level test above.
    [Fact]
    public void DeepPooledTraversalAllocatesNothingOnceWarmAndReturnsItsStackOnEveryExit()
    {
        Scan scan = Bind();
        byte[] valid = [.. Enumerable.Repeat((byte)0x81, 10000), 0x00];
        byte[] invalid = [.. Enumerable.Repeat((byte)0x9f, 10000), 0x1c];
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(valid.Length, scan(valid));
            Assert.Equal(0, scan(valid.AsSpan(0, 10000)));
            Assert.Equal(-1, scan(invalid));
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 100; i++)
        {
            sum += scan(valid);
            sum += scan(valid.AsSpan(0, 10000));
            sum += scan(invalid);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1000000, sum);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CompleteAndPartialFixturesAllocateZeroBytes()
    {
        Scan scan = Bind();
        byte[][] fixtures = [Convert.FromHexString("8204D81843010203"), Convert.FromHexString("9F5F4101FFFF"), Convert.FromHexString("9F5F4101")];
        for (int i = 0; i < 100; i++) { foreach (byte[] fixture in fixtures) { _ = scan(fixture); } }
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 1000; i++) { foreach (byte[] fixture in fixtures) { sum += scan(fixture); } }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(14000, sum);
        Assert.Equal(0, allocated);
    }
}

using System.Reflection;
using System.Buffers.Binary;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

public class CborFrameScannerTests
{
    private delegate int Scan(ReadOnlySpan<byte> bytes);

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
        Scan scan = CborFrameScanner.GetFrameLength;
        byte[] bytes = Convert.FromHexString(hex);
        Assert.Equal(length, scan(bytes));
        AssertResumable(bytes, length);
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
    public void RejectsMalformedAndOverlongDeclarationsWithoutThrowing(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        Assert.Equal(-1, CborFrameScanner.GetFrameLength(bytes));
        AssertResumable(bytes, -1);
    }

    [Fact]
    public void RejectsTheReviewersImpossibleTwoItemFrame() => Assert.Equal(-1, CborFrameScanner.GetFrameLength(Convert.FromHexString("825A7FFFFFF9")));

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
        Scan scan = CborFrameScanner.GetFrameLength;
        uint largestRepresentable = (uint)((Array.MaxLength - fixedBytes) / bytesPerChild);
        // Headers alone are enough to distinguish a possible partial frame from one which can
        // never fit; the test never allocates the advertised multi-gigabyte bodies or children.
        Assert.Equal(0, scan(LengthPrefix(prefix, initial, largestRepresentable)));
        Assert.Equal(-1, scan(LengthPrefix(prefix, initial, largestRepresentable + 1)));
        AssertResumable(LengthPrefix(prefix, initial, largestRepresentable), 0);
        AssertResumable(LengthPrefix(prefix, initial, largestRepresentable + 1), -1);
    }

    [Theory]
    [InlineData(0x5a)]
    [InlineData(0xc0)]
    [InlineData(0x9f)]
    public void MinimumFrameSizeIncludesPartialHeadersAndNewChildOrBreakBytes(byte initial)
    {
        Scan scan = CborFrameScanner.GetFrameLength;
        byte[] arrayAtLimit = LengthPrefix("", 0x9a, (uint)(Array.MaxLength - 5));
        Assert.Equal(0, scan(arrayAtLimit));
        // Every sibling already reserves its minimum one byte. A first item that needs more
        // header bytes, a tagged child, or an indefinite break makes that minimum impossible.
        Assert.Equal(-1, scan([.. arrayAtLimit, initial]));
        AssertResumable(arrayAtLimit, 0);
        AssertResumable([.. arrayAtLimit, initial], -1);
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
        Scan scan = CborFrameScanner.GetFrameLength;
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

    [Fact]
    public void ResumableSpillSurvivesHeadStringAndContainerSplits()
    {
        byte[] bytes = [.. Enumerable.Repeat((byte)0x9f, 100), 0x5f, 0x59, 0x00, 0x03, 1, 2, 3, 0xff, .. Enumerable.Repeat((byte)0xff, 100)];
        CborFrameScanner.State boundary = new();
        FieldInfo spill = typeof(CborFrameScanner.State).GetField("_spill", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            Assert.Equal(0, boundary.Scan(bytes.AsSpan(0, 63), out int consumed));
            Assert.Equal(63, consumed);
            Assert.Null(spill.GetValue(boundary));
            Assert.Equal(0, boundary.Scan(bytes.AsSpan(63, 1), out consumed));
            Assert.Equal(1, consumed);
            Assert.NotNull(spill.GetValue(boundary));
        }
        finally { boundary.Dispose(); }
        AssertResumable(bytes, bytes.Length);
        AssertResumable(Convert.FromHexString("825A7FFFFFF9"), -1);
    }

    private static void AssertResumable(byte[] bytes, int expected)
    {
        for (int split = 0; split <= bytes.Length; split++)
        {
            CborFrameScanner.State state = new();
            try
            {
                int first = state.Scan(bytes.AsSpan(0, split), out int consumed);
                Assert.InRange(consumed, 0, split);
                if (first >= 0) { Assert.Equal(first > 0 ? first : split, consumed); }
                int total = consumed;
                int result = first;
                if (first == 0)
                {
                    result = state.Scan(bytes.AsSpan(split), out consumed);
                    Assert.InRange(consumed, 0, bytes.Length - split);
                    if (result >= 0) { Assert.Equal(result > 0 ? result - split : bytes.Length - split, consumed); }
                    total += consumed;
                }
                Assert.Equal(expected, result);
                Assert.Equal(total, state.BytesScanned);
                if (result != 0)
                {
                    Assert.Equal(result, state.Scan(bytes, out consumed));
                    Assert.Equal(0, consumed);
                    Assert.Equal(total, state.BytesScanned);
                }
            }
            finally { state.Dispose(); }
        }
        CborFrameScanner.State singles = new();
        _ = singles.Scan(bytes, out _);
        singles.Dispose();
        Assert.Equal(0, singles.BytesScanned);
        try
        {
            int result = 0;
            for (int i = 0; i < bytes.Length && result == 0; i++)
            {
                result = singles.Scan(bytes.AsSpan(i, 1), out int consumed);
                Assert.Equal(1, consumed);
                Assert.Equal(i + 1, singles.BytesScanned);
            }
            Assert.Equal(expected, result);
        }
        finally { singles.Dispose(); }
    }

    // Allocation/pool-return guard only; the recursion-discriminating depth is the million-level test above.
    [Fact]
    public void DeepPooledTraversalAllocatesNothingOnceWarmAndReturnsItsStackOnEveryExit()
    {
        Scan scan = CborFrameScanner.GetFrameLength;
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
        Scan scan = CborFrameScanner.GetFrameLength;
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

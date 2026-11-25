using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;

namespace Chrysalis.Codec.Test;

public sealed class CollectionCapacityAndTrackerTests
{
    [Theory]
    [InlineData(-1, 4)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(10, 10)]
    [InlineData(16, 16)]
    [InlineData(256, 256)]
    [InlineData(257, 512)]
    [InlineData(300, 512)]
    public void DefiniteCapacityFitsSmallCollectionsAndGrowsPastTheClamp(int size, int listCapacity)
    {
        int count = size < 0 ? 3 : size;
        List<int> list = [.. Enumerable.Range(0, count)];
        Dictionary<int, bool> map = list.ToDictionary(key => key, key => (key & 1) == 0);
        if (size < 0)
        {
            IndefiniteStateTracker.SetIndefinite(list);
            IndefiniteStateTracker.SetIndefinite(map);
        }
        byte[] listWire = CborSerializer.Serialize(new X32EagerIntList(list));
        byte[] mapWire = CborSerializer.Serialize(new X32EagerIntBoolMap(map));
        Assert.Equal(size < 0, listWire[0] == 0x9f);
        Assert.Equal(size < 0, mapWire[0] == 0xbf);
        Dictionary<int, bool> reference = [];
        _ = reference.EnsureCapacity(Math.Clamp(size, 0, 256));
        foreach ((int key, bool value) in map) { _ = reference.TryAdd(key, value); }
        foreach (List<int> read in new[] { X32EagerIntList.Read(listWire).Value, X32LazyIntList.Read(listWire).Value })
        {
            Assert.Equal(list, read);
            // 257+ items have caused one growth from the initial 256-entry backing array.
            Assert.Equal(listCapacity, read.Capacity);
        }
        foreach (Dictionary<int, bool> read in new[] { X32EagerIntBoolMap.Read(mapWire).Value, X32LazyIntBoolMap.Read(mapWire).Value })
        {
            Assert.Equal(map, read);
            Assert.Equal(reference.EnsureCapacity(0), read.EnsureCapacity(0));
        }
    }

    [Theory]
    [InlineData("9A7FFFFFFF")]
    [InlineData("BA7FFFFFFF")]
    [InlineData("9A00010000")]
    [InlineData("BA00010000")]
    [InlineData("9A80000000")]
    [InlineData("BA80000000")]
    [InlineData("9BFFFFFFFFFFFFFFFF")]
    [InlineData("BBFFFFFFFFFFFFFFFF")]
    public void UntrustedWireClaimStaysBelow64KiB(string header)
    {
        byte[] wire = Convert.FromHexString(header);
        Action read = (wire[0] & 0xE0) == 0x80
            ? () => _ = X32EagerIntList.Read(wire)
            : () => _ = X32EagerIntBoolMap.Read(wire);
        void Decode() => _ = Assert.Throws<CborDecodeException>(read);
        long allocated = AllocationAssert.Measure(Decode, Decode);
        Assert.True(allocated < 64 * 1024, $"Malformed {header} allocated {allocated} B.");
    }

    [Fact]
    public void ShapeMismatchesKeepTypedErrors()
    {
        CborDecodeException list = Assert.Throws<CborDecodeException>(() => TypedShape.Read(Convert.FromHexString("8180")));
        CborDecodeException map = Assert.Throws<CborDecodeException>(() => TypedMapShape.Read(Convert.FromHexString("81BFFF")));
        Assert.Equal((1, "Chrysalis.Codec.Test.TypedShape.Value", "indefinite array", "definite array"), (list.Offset, list.TypePath, list.Expected, list.Actual));
        Assert.Equal((1, "Chrysalis.Codec.Test.TypedMapShape.Value", "definite map", "indefinite map"), (map.Offset, map.TypePath, map.Expected, map.Actual));
    }

    [Fact]
    public void TrackerUsesOneStatelessSentinelAndIsIdempotent()
    {
        object first = new();
        object second = new();
        IndefiniteStateTracker.SetIndefinite(null!);
        Assert.False(IndefiniteStateTracker.IsIndefinite(null!));
        Assert.False(IndefiniteStateTracker.IsIndefinite(first));
        IndefiniteStateTracker.SetIndefinite(first);
        IndefiniteStateTracker.SetIndefinite(second);
        object sentinel = SentinelFor(first);
        Assert.Same(sentinel, SentinelFor(second));
        IndefiniteStateTracker.SetIndefinite(first);
        Assert.Same(sentinel, SentinelFor(first));
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.DoesNotContain(sentinel.GetType().GetFields(instance), field => !field.IsInitOnly);
        Assert.DoesNotContain(sentinel.GetType().GetProperties(instance), property => property.CanWrite);
    }

    [Fact]
    public void TrackerDoesNotRootKeys()
    {
        WeakReference tracked = TrackEphemeralKey();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(tracked.IsAlive);
    }

    [Fact]
    public void TrackerHandlesEightConcurrentWritersAndReaders()
    {
        object shared = new();
        using Barrier start = new(8);
        bool[] seen = new bool[8];
        Thread[] threads = [.. Enumerable.Range(0, 8).Select(index => new Thread(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < 1000; i++)
            {
                IndefiniteStateTracker.SetIndefinite(shared);
                if (!IndefiniteStateTracker.IsIndefinite(shared)) { return; }
            }
            seen[index] = true;
        }))];
        foreach (Thread thread in threads) { thread.Start(); }
        foreach (Thread thread in threads) { thread.Join(); }
        Assert.All(seen, static value => Assert.True(value));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TrackEphemeralKey()
    {
        object key = new();
        IndefiniteStateTracker.SetIndefinite(key);
        Assert.True(IndefiniteStateTracker.IsIndefinite(key));
        return new WeakReference(key);
    }

    private static object SentinelFor(object key)
    {
        object table = typeof(IndefiniteStateTracker).GetField("_states", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object?[] arguments = [key, null];
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments)!);
        return arguments[1]!;
    }
}

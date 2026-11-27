using System.Buffers;
using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Utils;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

public class Tier2aCborMapTests
{
    static Tier2aCborMapTests()
    {
        CborCodecCache.RegisterStructReader(CollidingKey.Read);
        CborCodecCache.RegisterStructReader(NullAcceptingStruct.Read);
        CborCodecCache.RegisterReader(NullAcceptingClass.Read);
    }

    private delegate CborMap<TKey, TValue> ReadCoreDelegate<TKey, TValue>(
        ReadOnlyMemory<byte> data, out int bytesConsumed, out int probeSteps, out int keyComparisons)
        where TKey : notnull;

    private static ReadCoreDelegate<TKey, TValue> BindReadCore<TKey, TValue>() where TKey : notnull =>
        typeof(CborMap<TKey, TValue>)
            .GetMethod("ReadCore", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<ReadCoreDelegate<TKey, TValue>>();

    [Fact]
    public void ValueTypesSizeAndMigratedModelProperties()
    {
        Assert.True(typeof(CborMap<int, int>).IsValueType);
        Assert.Equal(32, Unsafe.SizeOf<CborMap<int, int>>());
        Assert.True(typeof(AuxiliaryDataSet).IsValueType);
        Assert.True(typeof(Withdrawals).IsValueType);

        Type[] models =
        [
            typeof(MultiAssetOutput),
            typeof(MultiAssetMint),
            typeof(TokenBundleOutput),
            typeof(TokenBundleMint),
            typeof(RedeemerMap),
            typeof(AuxiliaryDataSet),
            typeof(Withdrawals)
        ];
        foreach (Type model in models)
        {
            Type valueType = model.GetProperty("Value")!.PropertyType;
            Assert.True(valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(CborMap<,>),
                $"{model.Name}.Value expected CborMap<,>, got {valueType}.");
        }
    }

    [Theory]
    [InlineData("A0", 0, false)]
    [InlineData("BFFF", 0, true)]
    [InlineData("BF0102FF", 1, true)]
    [InlineData("BF01020304FF", 2, true)]
    public void DefiniteAndIndefiniteRoundTripCountsAndTrailingBytes(string hex, int expectedCount, bool expectedIndefinite)
    {
        byte[] wire = Convert.FromHexString(hex);
        byte[] padded = [0xAA, .. wire, 0x7F];
        ReadOnlyMemory<byte> slice = padded.AsMemory(1);

        CborMap<int, int> map = CborMap<int, int>.Read(slice, out int consumed);
        Assert.Equal(wire.Length, consumed);
        Assert.Equal(0x7F, slice.Span[consumed]);
        Assert.Equal(expectedCount, map.Count);
        Assert.Equal(expectedCount == 0, map.IsEmpty);
        Assert.Equal(expectedIndefinite, map.IsIndefinite);
        Assert.Equal(wire, CborSerializer.Serialize(map));

        List<KeyValuePair<int, int>> direct = [];
        foreach (KeyValuePair<int, int> kv in map) { direct.Add(kv); }
        KeyValuePair<int, int>[] boxed = [.. map];
        Assert.Equal(direct, boxed);
        Assert.Equal(expectedCount, direct.Count);
    }

    [Fact]
    public void NestedIndefiniteMultiAssetOutputAndFreshSnapshotIsolation()
    {
        byte[] wire = Convert.FromHexString("BF41AABF416105FFFF");
        byte[] padded = [0xEE, .. wire, 0x7F];
        MultiAssetOutput decoded = MultiAssetOutput.Read(padded.AsMemory(1), out int consumed);
        Assert.Equal(wire.Length, consumed);
        Assert.Equal(0x7F, padded[1 + consumed]);
        Assert.True(decoded.Value.IsIndefinite);

        List<(byte[] Policy, byte[] Asset, ulong Qty)> direct = [];
        foreach ((ReadOnlyMemory<byte> p, TokenBundleOutput bundle) in decoded.Value)
        {
            Assert.True(bundle.Value.IsIndefinite);
            foreach ((ReadOnlyMemory<byte> a, ulong q) in bundle.Value)
            {
                direct.Add((p.ToArray(), a.ToArray(), q));
            }
        }
        (byte[] Policy, byte[] Asset, ulong Qty)[] boxed = [.. decoded.Value
            .SelectMany(pe => pe.Value.Value
                .Select(ae => (pe.Key.ToArray(), ae.Key.ToArray(), ae.Value)))];
        _ = Assert.Single(direct);
        _ = Assert.Single(boxed);
        Assert.Equal(direct[0].Policy, boxed[0].Policy);
        Assert.Equal(direct[0].Asset, boxed[0].Asset);
        Assert.Equal(5UL, direct[0].Qty);

        Dictionary<int, int> source = new() { [1] = 10, [2] = 20 };
        CborMap<int, int> definite = CborMap<int, int>.Create(source, isIndefinite: false);
        CborMap<int, int> indefinite = CborMap<int, int>.Create(source, isIndefinite: true);
        source[1] = 99;
        source[3] = 30;
        Assert.Equal(2, definite.Count);
        Assert.Equal(10, definite[1]);
        Assert.False(definite.ContainsKey(3));
        Assert.False(definite.IsIndefinite);
        Assert.True(indefinite.IsIndefinite);

        byte[] defBytes = CborSerializer.Serialize(definite);
        byte[] indefBytes = CborSerializer.Serialize(indefinite);
        Assert.Equal(0xA2, defBytes[0]);
        Assert.Equal(0xBF, indefBytes[0]);
        Assert.Equal(0xFF, indefBytes[^1]);
        Assert.Equal(20, CborMap<int, int>.Read(indefBytes)[2]);
    }

    [Fact]
    public void WarmEligibleOperationsAllocateZeroBytes()
    {
        MultiAssetOutput ma = MultiAssetOutput.Read(Convert.FromHexString("A141AAA1416105"));
        ReadOnlyMemory<byte> policy = new byte[] { 0xAA };
        ReadOnlyMemory<byte> asset = "a"u8.ToArray();
        ulong sum = 0;
        void WalkMa()
        {
            sum = 0;
            foreach ((_, TokenBundleOutput b) in ma.Value)
            {
                foreach ((_, ulong q) in b.Value) { sum += q; }
            }
        }
        long walkAlloc = AllocationAssert.Measure(() => { for (int i = 0; i < 64; i++) { WalkMa(); } }, WalkMa);
        Assert.Equal(5UL, sum);
        Assert.Equal(0, walkAlloc);

        ulong foundQty = 0;
        void ProbeMa() => foundQty = ma.Value.TryGetValue(policy, out TokenBundleOutput b) && b.Value.TryGetValue(asset, out ulong q) ? q : 0;
        long probeAlloc = AllocationAssert.Measure(() => { for (int i = 0; i < 64; i++) { ProbeMa(); } }, ProbeMa);
        Assert.Equal(5UL, foundQty);
        Assert.Equal(0, probeAlloc);

        IValue val = LovelaceWithMultiAsset.Read(Convert.FromHexString("821A001E8480A141AAA1416105"));
        ulong? qty = null;
        void ReadQty() => qty = val.QuantityOf(policy, asset);
        long qtyAlloc = AllocationAssert.Measure(() => { for (int i = 0; i < 64; i++) { ReadQty(); } }, ReadQty);
        Assert.Equal(5UL, qty);
        Assert.Equal(0, qtyAlloc);

        ConwayBlock block = ConwayBlock.Read(Convert.FromHexString("85808080A080"));
        int auxCount = -1;
        void ReadAux() => auxCount = block.AuxiliaryDataSet.Value.Count;
        long auxAlloc = AllocationAssert.Measure(() => { for (int i = 0; i < 64; i++) { ReadAux(); } }, ReadAux);
        Assert.Equal(0, auxCount);
        Assert.Equal(0, auxAlloc);

        RedeemerMap rm = RedeemerMap.Read(Convert.FromHexString("A18200008201820100"));
        RedeemerKey rk = RedeemerKey.Create(0, 0);
        ulong mem = 0;
        void ProbeRm() => mem = rm.Value.TryGetValue(rk, out RedeemerValue rv) ? rv.ExUnits.Mem : 0;
        long rmAlloc = AllocationAssert.Measure(() => { for (int i = 0; i < 64; i++) { ProbeRm(); } }, ProbeRm);
        Assert.Equal(1UL, mem);
        Assert.Equal(0, rmAlloc);

        // Large unique map (N = 100 > 32 stack limit): pooled Read + foreach + TryGetValue allocate 0 B warm.
        ArrayBufferWriter<byte> buf = new();
        CborWriter writer = new(buf);
        writer.WriteBeginMap(100);
        for (int i = 0; i < 100; i++)
        {
            byte[] key = [(byte)(i >> 8), (byte)i]; writer.WriteByteString(key);
            writer.WriteUInt64((ulong)(i + 1));
        }
        writer.WriteEndMap(100);
        ReadOnlyMemory<byte> largeWire = buf.WrittenMemory.ToArray();
        ReadOnlyMemory<byte> targetKey = new byte[] { 0, 77 };
        ulong largeSum = 0;
        ulong largeHit = 0;
        void LargeReadWalkProbe()
        {
            TokenBundleOutput bundle = TokenBundleOutput.Read(largeWire);
            CborMap<ReadOnlyMemory<byte>, ulong> map = bundle.Value;
            largeSum = 0;
            foreach ((_, ulong q) in map) { largeSum += q; }
            _ = map.TryGetValue(targetKey, out largeHit);
        }
        long largeAlloc = AllocationAssert.Measure(() => { for (int i = 0; i < 64; i++) { LargeReadWalkProbe(); } }, LargeReadWalkProbe);
        Assert.Equal(5050UL, largeSum);
        Assert.Equal(78UL, largeHit);
        Assert.Equal(0, largeAlloc);
    }

    [Fact]
    public void DefiniteKeyBorrowsBackingStorageWhileIndefiniteKeyIsOwned()
    {
        byte[] wire = Convert.FromHexString("A2421122015F41AA41BBFF02");
        byte[] buf = [0xFF, 0xEE, .. wire, 0xDD];
        ReadOnlyMemory<byte> slice = buf.AsMemory(2, wire.Length);
        CborMap<ReadOnlyMemory<byte>, ulong> map = CborMap<ReadOnlyMemory<byte>, ulong>.Read(slice);

        KeyValuePair<ReadOnlyMemory<byte>, ulong>[] entries = [.. map];
        Assert.Equal(2, entries.Length);
        Assert.Equal(Convert.FromHexString("1122"), entries[0].Key.ToArray());
        Assert.Equal(Convert.FromHexString("AABB"), entries[1].Key.ToArray());

        buf.AsSpan().Fill(0x77);
        Assert.Equal(Convert.FromHexString("7777"), entries[0].Key.ToArray());
        Assert.Equal(Convert.FromHexString("AABB"), entries[1].Key.ToArray());
    }

    [Fact]
    public void InterleavedDuplicateRedeemerMapKeepsFirstKeyAndPositionWithLastValue()
    {
        // A = 820000 (spend,0) -> ExUnits(1,0); B = 820100 (mint,0) -> ExUnits(5,0); A' = 9F0000FF (spend,0) -> ExUnits(9,0)
        const string dupHex = "A3820000820182010082010082028205009F0000FF8203820900";
        byte[] wire = Convert.FromHexString(dupHex);
        RedeemerMap decoded = RedeemerMap.Read(wire);
        CborMap<RedeemerKey, RedeemerValue> map = decoded.Value;

        Assert.Equal(2, map.Count);
        Assert.True(CborMap<RedeemerKey, RedeemerValue>.Read(wire) == CborMap<RedeemerKey, RedeemerValue>.Read(wire));

        List<KeyValuePair<RedeemerKey, RedeemerValue>> direct = [];
        foreach (KeyValuePair<RedeemerKey, RedeemerValue> kv in map) { direct.Add(kv); }
        KeyValuePair<RedeemerKey, RedeemerValue>[] boxed = [.. map];

        foreach (IReadOnlyList<KeyValuePair<RedeemerKey, RedeemerValue>> seq in new IReadOnlyList<KeyValuePair<RedeemerKey, RedeemerValue>>[] { direct, boxed })
        {
            Assert.Equal(2, seq.Count);
            Assert.Equal((0, 0UL), (seq[0].Key.Tag, seq[0].Key.Index));
            Assert.Equal(Convert.FromHexString("820000"), seq[0].Key.Raw.ToArray());
            Assert.Equal(9UL, seq[0].Value.ExUnits.Mem);
            Assert.Equal((1, 0UL), (seq[1].Key.Tag, seq[1].Key.Index));
            Assert.Equal(5UL, seq[1].Value.ExUnits.Mem);
        }

        RedeemerKey keyDef = RedeemerKey.Read(Convert.FromHexString("820000"));
        RedeemerKey keyIndef = RedeemerKey.Read(Convert.FromHexString("9F0000FF"));
        Assert.True(map.TryGetValue(keyDef, out RedeemerValue v1));
        Assert.True(map.TryGetValue(keyIndef, out RedeemerValue v2));
        Assert.Equal(9UL, v1.ExUnits.Mem);
        Assert.Equal(9UL, v2.ExUnits.Mem);
        Assert.Equal(2, map.ToDictionary().Count);
        Assert.Equal(9UL, map.ToDictionary()[keyDef].ExUnits.Mem);

        // Real InputExtensions.Redeemer must return A (Tag 0, Mem 9) in position 0 with A's exact wire key bytes.
        TransactionInput input0 = TransactionInput.Create(new byte[32], 0);
        ConwayBlock block = ConwayBlock.Read(Convert.FromHexString(
            $"858081A30081{Convert.ToHexString(input0.Raw.Span)}0180020081A105{dupHex}A080"));
        RedeemerEntry resolved = Assert.IsType<RedeemerEntry>(input0.Redeemer(block));
        Assert.Equal(0, resolved.Tag);
        Assert.Equal(0UL, resolved.Index);
        Assert.Equal(9UL, resolved.ExUnits.Mem);
        Assert.Equal(Convert.FromHexString("84000003820900"), resolved.Raw.ToArray());

        // Decoded preserves original 3-entry wire verbatim; fresh Create folds [A:1, B:5, A':9] -> [A:9, B:5].
        Assert.Equal(wire, CborSerializer.Serialize(decoded));
        RedeemerValue val1 = RedeemerValue.Read(Convert.FromHexString("8201820100"));
        RedeemerValue val5 = RedeemerValue.Read(Convert.FromHexString("8202820500"));
        RedeemerValue val9 = RedeemerValue.Read(Convert.FromHexString("8203820900"));
        RedeemerMap fresh = RedeemerMap.Create(CborMap<RedeemerKey, RedeemerValue>.Create(
            [new(keyDef, val1), new(RedeemerKey.Read(Convert.FromHexString("820100")), val5), new(keyIndef, val9)]));
        Assert.Equal(2, fresh.Value.Count);
        Assert.Equal(Convert.FromHexString("A282000082038209008201008202820500"), CborSerializer.Serialize(fresh));

        // Warm direct foreach on duplicate RedeemerMap allocates 0 B (reuses stored int[] index).
        ulong dupSum = 0;
        void WalkDup()
        {
            dupSum = 0;
            foreach ((_, RedeemerValue rv) in map) { dupSum += rv.ExUnits.Mem; }
        }
        long dupWalkAlloc = AllocationAssert.Measure(() => { for (int i = 0; i < 64; i++) { WalkDup(); } }, WalkDup);
        Assert.Equal(14UL, dupSum);
        Assert.Equal(0, dupWalkAlloc);
    }

    [Fact]
    public void SmallLargeAndConsumerDuplicateMapsKeepLastValueOnce()
    {
        // Indefinite duplicate map
        byte[] indefDup = Convert.FromHexString("BF01020103FF");
        CborMap<int, int> indefMap = CborMap<int, int>.Read(indefDup);
        Assert.Equal(1, indefMap.Count);
        Assert.Equal(3, indefMap[1]);
        Assert.Equal(indefDup, CborSerializer.Serialize(indefMap));

        // Duplicate Withdrawals
        string acctHex = "E0" + new string('1', 56);
        byte[] wdWire = Convert.FromHexString($"A2581D{acctHex}01581D{acctHex}07");
        Withdrawals wd = Withdrawals.Read(wdWire);
        Assert.Equal(1, wd.Value.Count);
        Assert.Equal(7UL, wd.Value[new RewardAccount(Convert.FromHexString(acctHex))]);
        Assert.Equal(wdWire, CborSerializer.Serialize(wd));

        // Large duplicate map (wireCount == 40, distinctCount == 36 > 32)
        ArrayBufferWriter<byte> buf = new();
        CborWriter writer = new(buf);
        writer.WriteBeginMap(40);
        for (int i = 0; i < 36; i++)
        {
            byte[] key = [(byte)i]; writer.WriteByteString(key);
            writer.WriteUInt64((ulong)(i + 1));
        }
        for (int i = 0; i < 4; i++)
        {
            byte[] key = [(byte)i]; writer.WriteByteString(key);
            writer.WriteUInt64((ulong)(100 + i));
        }
        writer.WriteEndMap(40);
        byte[] largeDupWire = buf.WrittenMemory.ToArray();
        CborMap<ReadOnlyMemory<byte>, ulong> largeDup = CborMap<ReadOnlyMemory<byte>, ulong>.Read(largeDupWire);
        Assert.Equal(36, largeDup.Count);
        Assert.Equal(largeDupWire, CborSerializer.Serialize(largeDup));
        KeyValuePair<ReadOnlyMemory<byte>, ulong>[] directArr = [.. largeDup];
        Assert.Equal(36, directArr.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal((byte)i, directArr[i].Key.Span[0]);
            Assert.Equal((ulong)(100 + i), directArr[i].Value);
        }
        ulong largeDupSum = 0;
        void WalkLargeDup()
        {
            largeDupSum = 0;
            foreach ((_, ulong q) in largeDup) { largeDupSum += q; }
        }
        long largeDupWalkAlloc = AllocationAssert.Measure(() => { for (int i = 0; i < 64; i++) { WalkLargeDup(); } }, WalkLargeDup);
        Assert.Equal(0, largeDupWalkAlloc);
        Assert.True(largeDupSum > 400);

        // Nested duplicate policy + duplicate asset in ValueExtensions (no double counting!)
        byte[] maDupWire = Convert.FromHexString("821A001E8480A241AAA141610141AAA2416102416105");
        IValue maVal = LovelaceWithMultiAsset.Read(maDupWire);
        ReadOnlyMemory<byte> pol = new byte[] { 0xAA };
        ReadOnlyMemory<byte> ast = "a"u8.ToArray();
        Assert.Equal(5UL, maVal.QuantityOf(pol, ast));
        Assert.Equal(5UL, maVal.ToAssetDictionary()["aa61"]);
        (string PolicyId, string AssetName, ulong Quantity) only = Assert.Single(maVal.ToAssetList());
        Assert.Equal(("aa", "61", 5UL), only);
        IValue merged = maVal.Merge(Lovelace.Create(1));
        Assert.Equal(5UL, merged.QuantityOf(pol, ast));
        IValue subtracted = maVal.Subtract(LovelaceWithMultiAsset.Read(Convert.FromHexString("8200A141AAA1416102")));
        Assert.Equal(3UL, subtracted.QuantityOf(pol, ast));
    }

    [Fact]
    public void CollisionAndWorkCountsDeterministicAndSeededRegressionThresholds()
    {
        // Empty map initializes counters to 0.
        ReadCoreDelegate<int, int> readInt = BindReadCore<int, int>();
        CborMap<int, int> empty = readInt(Convert.FromHexString("A0"), out int emptyConsumed, out int emptyProbes, out int emptyComps);
        Assert.Equal(1, emptyConsumed);
        Assert.Equal(0, empty.Count);
        Assert.Equal(0, emptyProbes);
        Assert.Equal(0, emptyComps);

        // Seed-independent deterministic CollidingKey (64 entries -> exact 64*63/2 = 2016 key comparisons across growth!).
        ArrayBufferWriter<byte> colBuf = new();
        CborWriter colWriter = new(colBuf);
        colWriter.WriteBeginMap(64);
        for (int i = 0; i < 64; i++)
        {
            colWriter.WriteInt32(i);
            colWriter.WriteInt32(i * 10);
        }
        colWriter.WriteEndMap(64);
        ReadCoreDelegate<CollidingKey, int> readCol = BindReadCore<CollidingKey, int>();
        CborMap<CollidingKey, int> colMap = readCol(colBuf.WrittenMemory, out int colConsumed, out int colProbes, out int colComps);
        Assert.Equal(colBuf.WrittenCount, colConsumed);
        Assert.Equal(64, colMap.Count);
        Assert.Equal(2016, colComps);
        Assert.True(colProbes >= 2016, $"Expected probeSteps >= 2016, got {colProbes}.");
        int seen = 0;
        foreach ((CollidingKey k, int v) in colMap)
        {
            Assert.Equal(seen, k.Value);
            Assert.Equal(seen * 10, v);
            Assert.True(colMap.TryGetValue(k, out int lookedUp));
            Assert.Equal(v, lookedUp);
            seen++;
        }
        Assert.Equal(64, seen);

        // Shared 32-byte prefix ReadOnlyMemory<byte> seeded regression threshold (kills control-m7).
        ArrayBufferWriter<byte> prefixBuf = new();
        CborWriter prefixWriter = new(prefixBuf);
        prefixWriter.WriteBeginMap(200);
        byte[] keyBytes = new byte[64];
        keyBytes.AsSpan(0, 32).Fill(0xAB);
        for (int i = 0; i < 200; i++)
        {
            keyBytes[32] = (byte)(i >> 8);
            keyBytes[33] = (byte)i;
            prefixWriter.WriteByteString(keyBytes);
            prefixWriter.WriteInt32(i);
        }
        prefixWriter.WriteEndMap(200);
        ReadCoreDelegate<ReadOnlyMemory<byte>, int> readMem = BindReadCore<ReadOnlyMemory<byte>, int>();
        CborMap<ReadOnlyMemory<byte>, int> prefixMap = readMem(prefixBuf.WrittenMemory, out _, out int prefixProbes, out int prefixComps);
        Assert.Equal(200, prefixMap.Count);
        Assert.True(prefixComps <= 32, $"Shared-prefix seeded regression threshold exceeded: keyComparisons={prefixComps}.");
        Assert.True(prefixProbes <= 8 * 200, $"Shared-prefix seeded regression threshold exceeded: probeSteps={prefixProbes}.");

        // Strided int keys (0, 512, 1024, ...) seeded regression threshold.
        ArrayBufferWriter<byte> stridedBuf = new();
        CborWriter stridedWriter = new(stridedBuf);
        stridedWriter.WriteBeginMap(200);
        for (int i = 0; i < 200; i++)
        {
            stridedWriter.WriteInt32(i * 512);
            stridedWriter.WriteInt32(i);
        }
        stridedWriter.WriteEndMap(200);
        CborMap<int, int> stridedMap = readInt(stridedBuf.WrittenMemory, out _, out int stridedProbes, out int stridedComps);
        Assert.Equal(200, stridedMap.Count);
        Assert.True(stridedComps <= 32, $"Strided-int seeded regression threshold exceeded: keyComparisons={stridedComps}.");
        Assert.True(stridedProbes <= 8 * 200, $"Strided-int seeded regression threshold exceeded: probeSteps={stridedProbes}.");
    }

    [Fact]
    public void LazyKeyAndValueErrorOffsetsRebaseOnceOnPaddedSlices()
    {
        // Padded later RedeemerKey with Tag = "x" (text header at byte 10 -> SAIB cursor offset 2 -> 9 + 2 = 11).
        byte[] mapWire = Convert.FromHexString("A28200008201820100826178008201820100");
        byte[] padded = [0xAA, 0xBB, 0xCC, .. mapWire, 0xDD];
        ReadOnlyMemory<byte> slice = padded.AsMemory(3, mapWire.Length);

        CborDecodeException directErr = Assert.Throws<CborDecodeException>(() => CborMap<RedeemerKey, RedeemerValue>.Read(slice));
        Assert.Equal((11, "CborMap.entry.Chrysalis.Codec.Types.Cardano.Core.TransactionWitness.RedeemerKey.Tag", "integer", "text string"),
            (directErr.Offset, directErr.TypePath, directErr.Expected, directErr.Actual));

        RedeemerMap rm = RedeemerMap.Read(slice);
        CborDecodeException modelErr = Assert.Throws<CborDecodeException>(() => _ = rm.Value);
        Assert.Equal((11, "Chrysalis.Codec.Types.Cardano.Core.TransactionWitness.RedeemerMap.Value.CborMap.entry.Chrysalis.Codec.Types.Cardano.Core.TransactionWitness.RedeemerKey.Tag", "integer", "text string"),
            (modelErr.Offset, modelErr.TypePath, modelErr.Expected, modelErr.Actual));

        // 1-entry RedeemerMap with bad Tag -> Offset == 3 (proves wireCount == 0 validates key).
        byte[] singleBad = Convert.FromHexString("A1826178008201820100");
        CborDecodeException singleErr = Assert.Throws<CborDecodeException>(() => CborMap<RedeemerKey, RedeemerValue>.Read(singleBad));
        Assert.Equal(3, singleErr.Offset);
        Assert.Equal("CborMap.entry.Chrysalis.Codec.Types.Cardano.Core.TransactionWitness.RedeemerKey.Tag", singleErr.TypePath);

        // Padded malformed scalar later key (A2 01 02 6161 03 -> keyStart = 3, SAIB reads header 61 -> offset 3+1=4).
        byte[] badScalar = Convert.FromHexString("A20102616103");
        byte[] padScalar = [0x11, 0x22, .. badScalar, 0x33];
        CborDecodeException scalarErr = Assert.Throws<CborDecodeException>(() => CborMap<int, int>.Read(padScalar.AsMemory(2, badScalar.Length)));
        Assert.Equal((4, "CborMap.entry.System.Int32", "integer", "text string"),
            (scalarErr.Offset, scalarErr.TypePath, scalarErr.Expected, scalarErr.Actual));

        // Matched vs unmatched malformed value: A2 41AA 01 41BB A0
        byte[] mixedVal = Convert.FromHexString("A241AA0141BBA0");
        MultiAssetOutput ma = MultiAssetOutput.Read(mixedVal);
        Assert.True(ma.Value.TryGetValue(new byte[] { 0xBB }, out TokenBundleOutput okBundle));
        Assert.Equal(0, okBundle.Value.Count);
        CborDecodeException matchedErr = Assert.Throws<CborDecodeException>(() => _ = ma.Value.TryGetValue(new byte[] { 0xAA }, out _));
        Assert.Equal(3, matchedErr.Offset);
        Assert.Equal("CborMap.entry.Chrysalis.Codec.Types.Cardano.Core.Common.TokenBundleOutput.Value", matchedErr.TypePath);
        CborDecodeException iterErr = Assert.Throws<CborDecodeException>(() => { foreach (KeyValuePair<ReadOnlyMemory<byte>, TokenBundleOutput> _ in ma.Value) { } });
        Assert.Equal(3, iterErr.Offset);

        // Nested MultiAssetOutput -> TokenBundleOutput malformed value (A1 41AA A1 4161 6178)
        MultiAssetOutput nested = MultiAssetOutput.Read(Convert.FromHexString("A141AAA141616178"));
        TokenBundleOutput inner = nested.Value[new byte[] { 0xAA }];
        CborDecodeException nestedErr = Assert.Throws<CborDecodeException>(() => _ = inner.Value["a"u8.ToArray()]);
        Assert.Equal(4, nestedErr.Offset);
        Assert.Equal("CborMap.entry.System.UInt64", nestedErr.TypePath);
        Assert.Equal("unsigned integer", nestedErr.Expected);
        Assert.Equal("text string", nestedErr.Actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("80")]
    [InlineData("01")]
    [InlineData("40")]
    [InlineData("D818A0")]
    [InlineData("A101")]
    [InlineData("A2010203")]
    [InlineData("A101FF")]
    [InlineData("BF0102")]
    public void MalformedMapsThrowCborDecodeException(string hex)
    {
        byte[] wire = Convert.FromHexString(hex);
        _ = Assert.Throws<CborDecodeException>(() => CborMap<int, int>.Read(wire));
    }

    [Fact]
    public void OddElementIndefiniteAndUntrustedCountHeadersAreBoundedAndExact()
    {
        CborDecodeException oddBreak = Assert.Throws<CborDecodeException>(() => CborMap<int, int>.Read(Convert.FromHexString("BF01FF")));
        Assert.Equal((2, "CborMap", "map value", "simple value or floating point"),
            (oddBreak.Offset, oddBreak.TypePath, oddBreak.Expected, oddBreak.Actual));

        CborDecodeException oddEof = Assert.Throws<CborDecodeException>(() => CborMap<int, int>.Read(Convert.FromHexString("BF01")));
        Assert.Equal((2, "CborMap", "map value", "end of input"),
            (oddEof.Offset, oddEof.TypePath, oddEof.Expected, oddEof.Actual));

        foreach (string header in new[] { "BA7FFFFFFF", "BA00010000", "BA80000000", "BBFFFFFFFFFFFFFFFF" })
        {
            byte[] wire = Convert.FromHexString(header);
            void Decode() => _ = Assert.Throws<CborDecodeException>(() => CborMap<int, int>.Read(wire));
            long allocated = AllocationAssert.Measure(Decode, Decode);
            Assert.True(allocated < 64 * 1024, $"Malformed {header} allocated {allocated} B.");
        }
    }

    [Fact]
    public void ByteArrayKeysUseContentEqualityAfterRedecoding()
    {
        CborMap<byte[], int> map = CborSerializer.Deserialize<CborMap<byte[], int>>(Convert.FromHexString("A141AA01"));
        Assert.Equal(1, map.Count);
        foreach (KeyValuePair<byte[], int> entry in map)
        {
            Assert.True(map.ContainsKey(entry.Key));
            Assert.True(map.TryGetValue(entry.Key, out int value));
            Assert.Equal(1, value);
        }
        Assert.Equal(1, map[[0xAA]]);
    }

    [Fact]
    public void DuplicateByteArrayKeysHaveOneEntryWithLastValue()
    {
        CborMap<byte[], int> map = CborSerializer.Deserialize<CborMap<byte[], int>>(Convert.FromHexString("A241AA0141AA02"));
        Assert.Equal(1, map.Count);
        Assert.Equal(2, Assert.Single(map).Value);
        Assert.True(map.TryGetValue([0xAA], out int value));
        Assert.Equal(2, value);
    }

    [Fact]
    public void FreshMapWritesPresentNullableWithdrawalBeforeConcreteWrite()
    {
        byte[] wire = Convert.FromHexString("A101A0");
        CborMap<int, Withdrawals?> decoded = CborSerializer.Deserialize<CborMap<int, Withdrawals?>>(wire);
        Assert.True(decoded[1] is { } withdrawal && withdrawal.Value.IsEmpty);

        CborMap<int, Withdrawals?> rebuilt = CborMap<int, Withdrawals?>.Create(decoded.ToDictionary());
        byte[] reencoded = CborSerializer.Serialize(rebuilt);
        Assert.Equal(wire, reencoded);
        CborMap<int, Withdrawals?> reread = CborSerializer.Deserialize<CborMap<int, Withdrawals?>>(reencoded);
        Assert.True(reread[1] is { } roundTripped && roundTripped.Value.IsEmpty);
        Assert.Equal(new byte[] { 0xA0 }, CborSerializer.Serialize(Withdrawals.Create([])));

        _ = Assert.Throws<CborValidationException>(() => CborMap<int, Withdrawals?>.Create([new(1, null)]));
    }

    [Fact]
    public void FreshListWritesPresentNullableWithdrawalAfterConcreteWrite()
    {
        Assert.Equal(new byte[] { 0xA0 }, CborSerializer.Serialize(Withdrawals.Create([])));

        byte[] wire = Convert.FromHexString("81A0");
        CborList<Withdrawals?> decoded = CborSerializer.Deserialize<CborList<Withdrawals?>>(wire);
        Assert.True(Assert.Single(decoded) is { } withdrawal && withdrawal.Value.IsEmpty);

        byte[] reencoded = CborSerializer.Serialize(CborList.Create(decoded));
        Assert.Equal(wire, reencoded);
        CborList<Withdrawals?> reread = CborSerializer.Deserialize<CborList<Withdrawals?>>(reencoded);
        Assert.True(Assert.Single(reread) is { } roundTripped && roundTripped.Value.IsEmpty);
    }

    [Fact]
    public void CborEncodedValueFacadeNullItemsAndNullableDeserializeBothOrders()
    {
        CborMap<int, CborEncodedValue> encodedMap = CborMap<int, CborEncodedValue>.Create(
            [new(0, new CborEncodedValue(new byte[] { 0x01 }))]);
        byte[] mapBytes = CborSerializer.Serialize(encodedMap);
        Assert.Equal(Convert.FromHexString("A100D8184101"), mapBytes);
        Assert.Equal(new byte[] { 0x01 }, CborMap<int, CborEncodedValue>.Read(mapBytes)[0].Value.ToArray());
        CborMap<int, CborEncodedValue> decodedEncodedMap = CborMap<int, CborEncodedValue>.Create(
            [new(0, CborEncodedValue.Read(Convert.FromHexString("D8185F4101FF")))]);
        Assert.Equal(Convert.FromHexString("A100D8185F4101FF"), CborSerializer.Serialize(decodedEncodedMap));

        Assert.Equal(Convert.FromHexString("D8184101"), CborSerializer.Serialize(new CborEncodedValue(new byte[] { 0x01 })));
        CborEncodedValue emptyEncoded = new(default);
        Assert.Equal(Convert.FromHexString("D81840"), CborSerializer.Serialize(emptyEncoded));
        Assert.Equal(3, CborSerializer.GetEncodedLength(emptyEncoded));

        _ = Assert.Throws<CborValidationException>(() => CborMap<int, string?>.Create([new(1, null)]));
        CborMap<int, string?> wireNull = CborMap<int, string?>.Read(Convert.FromHexString("A101F6"));
        _ = Assert.Throws<CborDecodeException>(() => { foreach (KeyValuePair<int, string?> _ in wireNull) { } });
        _ = Assert.Throws<CborDecodeException>(() => _ = wireNull.TryGetValue(1, out _));

        // D9 both call orders: Withdrawals? -> Withdrawals, and AuxiliaryDataSet -> AuxiliaryDataSet?
        byte[] wdTrailing = Convert.FromHexString("A07F");
        Withdrawals? wdNullable = CborSerializer.Deserialize<Withdrawals?>(Convert.FromHexString("A0"));
        Assert.True(wdNullable.HasValue && wdNullable.Value.Value.IsEmpty);
        Withdrawals? wdNullableConsumed = CborSerializer.Deserialize<Withdrawals?>(wdTrailing, out int wdC1);
        Assert.True(wdNullableConsumed.HasValue);
        Assert.Equal(1, wdC1);
        Withdrawals wdNonNull = CborSerializer.Deserialize<Withdrawals>(Convert.FromHexString("A0"));
        Assert.True(wdNonNull.Value.IsEmpty);
        _ = CborSerializer.Deserialize<Withdrawals>(wdTrailing, out int wdC2);
        Assert.Equal(1, wdC2);
        _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<Withdrawals?>(Convert.FromHexString("F6")));

        byte[] auxTrailing = Convert.FromHexString("A07F");
        AuxiliaryDataSet auxNonNull = CborSerializer.Deserialize<AuxiliaryDataSet>(Convert.FromHexString("A0"));
        Assert.True(auxNonNull.Value.IsEmpty);
        _ = CborSerializer.Deserialize<AuxiliaryDataSet>(auxTrailing, out int auxC1);
        Assert.Equal(1, auxC1);
        AuxiliaryDataSet? auxNullable = CborSerializer.Deserialize<AuxiliaryDataSet?>(Convert.FromHexString("A0"));
        Assert.True(auxNullable.HasValue && auxNullable.Value.Value.IsEmpty);
        AuxiliaryDataSet? auxNullableConsumed = CborSerializer.Deserialize<AuxiliaryDataSet?>(auxTrailing, out int auxC2);
        Assert.True(auxNullableConsumed.HasValue);
        Assert.Equal(1, auxC2);
        _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<AuxiliaryDataSet?>(Convert.FromHexString("F6")));
    }

    [Fact]
    public void CollectionBuilderDefaultColdStandaloneFallbackAndKeyComparerParity()
    {
        CborMap<int, int> empty = [];
        Assert.Equal(0, empty.Count);
        Assert.True(empty.IsEmpty);
        Assert.Equal(new byte[] { 0xA0 }, CborSerializer.Serialize(empty));
        Assert.Equal(new byte[] { 0xA0 }, CborSerializer.Serialize(RedeemerMap.Create([])));

        CborMap<int, int> missing = default;
        Assert.Equal(0, missing.Count);
        Assert.True(missing.IsEmpty);
        Assert.False(missing == empty);
        Assert.False(missing.TryGetValue(1, out _));
        Assert.Empty(missing);
        CborValidationException writeErr = Assert.Throws<CborValidationException>(() =>
            CborMap<int, int>.Write(new ArrayBufferWriter<byte>(), missing));
        Assert.Equal((-1, "CborMap", "decoded map or map built from entries", "default value"),
            (writeErr.Offset, writeErr.TypePath, writeErr.Expected, writeErr.Actual));
        _ = Assert.Throws<ArgumentNullException>(() => CborMap<int, int>.Create(null!));

        // Cold standalone non-primitive registration.
        Assert.Null(CborCodecCache.GetReader<ColdStandaloneEntry>());
        byte[] coldWire = Convert.FromHexString("A105097F");
        CborDecodeException unregistered = Assert.Throws<CborDecodeException>(() =>
            _ = CborMap<ColdStandaloneEntry, ColdStandaloneEntry>.Read(coldWire, out _));
        Assert.Equal("unregistered type", unregistered.Actual);
        CborCodecCache.RegisterStructReader(ColdStandaloneEntry.Read);
        Assert.NotNull(CborCodecCache.GetReader<ColdStandaloneEntry>());
        CborMap<ColdStandaloneEntry, ColdStandaloneEntry> coldMap =
            CborMap<ColdStandaloneEntry, ColdStandaloneEntry>.Read(coldWire, out int coldConsumed);
        Assert.Equal(3, coldConsumed);
        KeyValuePair<ColdStandaloneEntry, ColdStandaloneEntry> coldPair = Assert.Single(coldMap);
        Assert.Equal(new ColdStandaloneEntry(5), coldPair.Key);
        Assert.Equal(new ColdStandaloneEntry(9), coldPair.Value);
        Assert.True(coldMap.TryGetValue(new ColdStandaloneEntry(5), out ColdStandaloneEntry coldHit));
        Assert.Equal(new ColdStandaloneEntry(9), coldHit);

        // KeyComparer parity across all 6 ledger key types.
        Assert.Same(ReadOnlyMemoryComparer.Instance, CborMap<ReadOnlyMemory<byte>, int>.KeyComparer);
        ReadOnlyMemory<byte> m1 = new byte[] { 1, 2, 3 };
        ReadOnlyMemory<byte> m2 = new byte[] { 1, 2, 3 };
        ReadOnlyMemory<byte> m3 = new byte[] { 1, 2, 4 };
        AssertComparerParity(CborMap<ReadOnlyMemory<byte>, int>.KeyComparer, m1, m2, m3);

        AssertComparerParity(CborMap<RewardAccount, int>.KeyComparer,
            new RewardAccount(new byte[] { 0xE0, 1 }),
            new RewardAccount(new byte[] { 0xE0, 1 }),
            new RewardAccount(new byte[] { 0xE0, 2 }));

        AssertComparerParity(CborMap<Credential, int>.KeyComparer,
            Credential.Read(Convert.FromHexString("8200421122")),
            Credential.Read(Convert.FromHexString("9F00421122FF")),
            Credential.Read(Convert.FromHexString("8201421122")));

        AssertComparerParity(CborMap<Voter, int>.KeyComparer,
            Voter.Read(Convert.FromHexString("8202421122")),
            Voter.Read(Convert.FromHexString("9F02421122FF")),
            Voter.Read(Convert.FromHexString("8203421122")));

        AssertComparerParity(CborMap<GovActionId, int>.KeyComparer,
            GovActionId.Read(Convert.FromHexString("8242112200")),
            GovActionId.Read(Convert.FromHexString("9F42112200FF")),
            GovActionId.Read(Convert.FromHexString("8242112201")));

        AssertComparerParity(CborMap<RedeemerKey, int>.KeyComparer,
            RedeemerKey.Read(Convert.FromHexString("820001")),
            RedeemerKey.Read(Convert.FromHexString("9F0001FF")),
            RedeemerKey.Read(Convert.FromHexString("820101")));
    }

    private static void AssertComparerParity<TKey>(IEqualityComparer<TKey> comparer, TKey a, TKey b, TKey diff) where TKey : notnull
    {
        Assert.True(comparer.Equals(a, b));
        Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
        Assert.False(comparer.Equals(a, diff));
    }

    private readonly struct CollidingKey(int value) : IEquatable<CollidingKey>
    {
        public int Value { get; } = value;
        public bool Equals(CollidingKey other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is CollidingKey other && Equals(other);
        public override int GetHashCode() => 42;
        public static CollidingKey Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
        {
            CborReader reader = new(data.Span);
            int val = reader.ReadInt32();
            bytesConsumed = data.Length - reader.Buffer.Length;
            return new(val);
        }
    }

    private readonly struct ColdStandaloneEntry(int value) : IEquatable<ColdStandaloneEntry>
    {
        public int Value { get; } = value;
        public bool Equals(ColdStandaloneEntry other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is ColdStandaloneEntry other && Equals(other);
        public override int GetHashCode() => Value;
        public static ColdStandaloneEntry Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
        {
            CborReader reader = new(data.Span);
            int val = reader.ReadInt32();
            bytesConsumed = data.Length - reader.Buffer.Length;
            return new(val);
        }
    }

    [Fact]
    public void WireNullShippedLazyStructValueIsRejectedOnAccessWhilePreservingRawAndContainsKey()
    {
        byte[] wire = Convert.FromHexString("A101F6");
        CborMap<int, PlutusBoundedBytes> map = CborMap<int, PlutusBoundedBytes>.Read(wire, out int consumed);
        Assert.Equal(wire.Length, consumed);
        Assert.Equal(1, map.Count);
        Assert.True(map.ContainsKey(1));
        Assert.Equal(wire, CborSerializer.Serialize(map));
        AssertWireNullMapAccessRejected(map, 2, CborError.Expected(typeof(PlutusBoundedBytes)));
    }

    [Fact]
    public void WireNullCustomStructAndReferenceValuesAreRejectedOnAccess()
    {
        byte[] nullWire = Convert.FromHexString("A101F6");
        AssertWireNullMapAccessRejected(CborMap<int, NullAcceptingStruct>.Read(nullWire), 2, CborError.Expected(typeof(NullAcceptingStruct)));
        AssertWireNullMapAccessRejected(CborMap<int, NullAcceptingClass>.Read(nullWire), 2, CborError.Expected(typeof(NullAcceptingClass)));

        byte[] okWire = Convert.FromHexString("A10105");
        CborMap<int, NullAcceptingStruct> okStruct = CborMap<int, NullAcceptingStruct>.Read(okWire);
        Assert.False(okStruct[1].SeenNull);
        CborMap<int, NullAcceptingClass> okClass = CborMap<int, NullAcceptingClass>.Read(okWire);
        Assert.False(okClass[1].SeenNull);
    }

    [Fact]
    public void WireNullReferenceAndNullableValuesAreRejectedAndNonNullNullableDecodes()
    {
        byte[] wire = Convert.FromHexString("A101F6");
        AssertWireNullMapAccessRejected(CborMap<int, string?>.Read(wire), 2, "text string");
        AssertWireNullMapAccessRejected(CborMap<int, PlutusBoundedBytes?>.Read(wire), 2, CborError.Expected(typeof(PlutusBoundedBytes?)));
        AssertWireNullMapAccessRejected(CborMap<int, Withdrawals?>.Read(wire), 2, CborError.Expected(typeof(Withdrawals?)));
        AssertWireNullMapAccessRejected(CborMap<int, int?>.Read(wire), 2, "integer");

        byte[] expectedAa = [0xAA];
        CborMap<int, PlutusBoundedBytes?> okBytes = CborMap<int, PlutusBoundedBytes?>.Read(Convert.FromHexString("A10141AA"));
        Assert.True(okBytes[1] is { } pbb && pbb.Value.Span.SequenceEqual(expectedAa));
        Assert.True(okBytes.TryGetValue(1, out PlutusBoundedBytes? pbbTry) && pbbTry is { } pbbVal && pbbVal.Value.Span.SequenceEqual(expectedAa));
        foreach (KeyValuePair<int, PlutusBoundedBytes?> kv in okBytes)
        {
            Assert.Equal(1, kv.Key);
            Assert.True(kv.Value is { } item && item.Value.Span.SequenceEqual(expectedAa));
        }
        Assert.True(Assert.Single(okBytes).Value is { } boxedPbb &&
            boxedPbb.Value.Span.SequenceEqual(expectedAa));

        CborMap<int, Withdrawals?> okWd = CborMap<int, Withdrawals?>.Read(Convert.FromHexString("A101A0"));
        Assert.True(okWd[1] is { } wd && wd.Value.IsEmpty);
        Assert.True(okWd.TryGetValue(1, out Withdrawals? wdTry) && wdTry is { } wdVal && wdVal.Value.IsEmpty);
        foreach (KeyValuePair<int, Withdrawals?> kv in okWd)
        {
            Assert.Equal(1, kv.Key);
            Assert.True(kv.Value is { } item && item.Value.IsEmpty);
        }
        Assert.True(Assert.Single(okWd).Value is { } boxedWd && boxedWd.Value.IsEmpty);

        CborMap<int, int?> okInt = CborMap<int, int?>.Read(Convert.FromHexString("A10105"));
        Assert.Equal(5, okInt[1]);
        Assert.True(okInt.TryGetValue(1, out int? intTry) && intTry == 5);
        foreach (KeyValuePair<int, int?> kv in okInt)
        {
            Assert.Equal((1, 5), (kv.Key, kv.Value));
        }
        Assert.Equal(5, Assert.Single(okInt).Value);
    }

    [Fact]
    public void WireNullKeysAreRejectedWithoutDoubleWrapping()
    {
        byte[] wire = Convert.FromHexString("A1F601");
        AssertWireNullException(() => _ = CborMap<PlutusBoundedBytes, int>.Read(wire), 1, CborError.Expected(typeof(PlutusBoundedBytes)));
        AssertWireNullException(() => _ = CborMap<string, int>.Read(wire), 1, "text string");
        AssertWireNullException(() => _ = CborMap<int, int>.Read(wire), 1, "integer");
        AssertWireNullException(() => _ = CborMap<NullAcceptingStruct, int>.Read(wire), 1, CborError.Expected(typeof(NullAcceptingStruct)));
        AssertWireNullException(() => _ = CborMap<NullAcceptingClass, int>.Read(wire), 1, CborError.Expected(typeof(NullAcceptingClass)));
    }

    [Fact]
    public void WireNullUnmatchedSkippingAndDuplicatePathsAreExact()
    {
        byte[] expectedAa = [0xAA];

        CborMap<int, PlutusBoundedBytes> skipped = CborMap<int, PlutusBoundedBytes>.Read(Convert.FromHexString("A201F60241AA"));
        Assert.Equal(2, skipped.Count);
        Assert.Equal(expectedAa, skipped[2].Value.ToArray());
        Assert.True(skipped.TryGetValue(2, out PlutusBoundedBytes hit2));
        Assert.Equal(expectedAa, hit2.Value.ToArray());
        AssertWireNullException(() => _ = skipped[1], 2, CborError.Expected(typeof(PlutusBoundedBytes)));
        AssertWireNullException(() => { foreach (KeyValuePair<int, PlutusBoundedBytes> _ in skipped) { } }, 2, CborError.Expected(typeof(PlutusBoundedBytes)));

        CborMap<int, PlutusBoundedBytes> superseded = CborMap<int, PlutusBoundedBytes>.Read(Convert.FromHexString("A201F60141AA"));
        Assert.Equal(1, superseded.Count);
        Assert.Equal(expectedAa, superseded[1].Value.ToArray());
        List<KeyValuePair<int, PlutusBoundedBytes>> supersededItems = [];
        foreach (KeyValuePair<int, PlutusBoundedBytes> kv in superseded) { supersededItems.Add(kv); }
        KeyValuePair<int, PlutusBoundedBytes> onlySuperseded = Assert.Single(supersededItems);
        Assert.Equal(1, onlySuperseded.Key);
        Assert.Equal(expectedAa, onlySuperseded.Value.Value.ToArray());

        byte[] dupNullWire = Convert.FromHexString("A20141AA01F6");
        CborMap<int, PlutusBoundedBytes> dupNull = CborMap<int, PlutusBoundedBytes>.Read(dupNullWire);
        Assert.Equal(1, dupNull.Count);
        Assert.True(dupNull.ContainsKey(1));
        Assert.Equal(dupNullWire, CborSerializer.Serialize(dupNull));
        AssertWireNullMapAccessRejected(dupNull, 5, CborError.Expected(typeof(PlutusBoundedBytes)));
    }

    private static void AssertWireNullException(Action action, int expectedOffset, string expectedType)
    {
        CborDecodeException error = Assert.Throws<CborDecodeException>(action);
        Assert.Equal((expectedOffset, "CborMap.entry", expectedType, "null"),
            (error.Offset, error.TypePath, error.Expected, error.Actual));
    }

    private static void AssertWireNullMapAccessRejected<TValue>(
        CborMap<int, TValue> map, int expectedOffset, string expectedType)
    {
        Assert.Equal(1, map.Count);
        Assert.True(map.ContainsKey(1));
        AssertWireNullException(() => _ = map[1], expectedOffset, expectedType);
        AssertWireNullException(() => _ = map.TryGetValue(1, out _), expectedOffset, expectedType);
        AssertWireNullException(() => { foreach (KeyValuePair<int, TValue> _ in map) { } }, expectedOffset, expectedType);
        AssertWireNullException(() =>
        {
            foreach (KeyValuePair<int, TValue> _ in (IEnumerable<KeyValuePair<int, TValue>>)map) { }
        }, expectedOffset, expectedType);
        AssertWireNullException(() => _ = map.ToDictionary(), expectedOffset, expectedType);
    }

    private readonly struct NullAcceptingStruct(bool seenNull) : IEquatable<NullAcceptingStruct>
    {
        public bool SeenNull { get; } = seenNull;
        public bool Equals(NullAcceptingStruct other) => SeenNull == other.SeenNull;
        public override bool Equals(object? obj) => obj is NullAcceptingStruct other && Equals(other);
        public override int GetHashCode() => SeenNull.GetHashCode();
        public static NullAcceptingStruct Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
        {
            CborReader reader = new(data.Span);
            bool isNull = !reader.Buffer.IsEmpty && reader.Buffer[0] == 0xF6;
            reader.SkipDataItem();
            bytesConsumed = data.Length - reader.Buffer.Length;
            return new(isNull);
        }
    }

    private sealed class NullAcceptingClass(bool seenNull) : IEquatable<NullAcceptingClass>
    {
        public bool SeenNull { get; } = seenNull;
        public bool Equals(NullAcceptingClass? other) => other is not null && SeenNull == other.SeenNull;
        public override bool Equals(object? obj) => obj is NullAcceptingClass other && Equals(other);
        public override int GetHashCode() => SeenNull.GetHashCode();
        public static NullAcceptingClass Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
        {
            CborReader reader = new(data.Span);
            bool isNull = !reader.Buffer.IsEmpty && reader.Buffer[0] == 0xF6;
            reader.SkipDataItem();
            bytesConsumed = data.Length - reader.Buffer.Length;
            return new(isNull);
        }
    }

    [Fact]
    public void FreshWireNullItemsAreRejectedOnCreateAndMutatedWrite()
    {
        PlutusBoundedBytes borrowedNull = PlutusBoundedBytes.Read(Convert.FromHexString("F6"));

        CborValidationException valErr = Assert.Throws<CborValidationException>(() =>
            CborMap<int, PlutusBoundedBytes>.Create([new(1, borrowedNull)]));
        Assert.Equal((-1, "CborMap", "non-null map value", "null"),
            (valErr.Offset, valErr.TypePath, valErr.Expected, valErr.Actual));

        CborValidationException keyErr = Assert.Throws<CborValidationException>(() =>
            CborMap<PlutusBoundedBytes, int>.Create([new(borrowedNull, 1)]));
        Assert.Equal((-1, "CborMap", "non-null map key", "null"),
            (keyErr.Offset, keyErr.TypePath, keyErr.Expected, keyErr.Actual));

        CborValidationException nullableValErr = Assert.Throws<CborValidationException>(() =>
            CborMap<int, PlutusBoundedBytes?>.Create([new(1, borrowedNull)]));
        Assert.Equal((-1, "CborMap", "non-null map value", "null"),
            (nullableValErr.Offset, nullableValErr.TypePath, nullableValErr.Expected, nullableValErr.Actual));

        RewardAccount account = new(Convert.FromHexString("E0" + new string('1', 56)));
        CborMap<RewardAccount, ulong> freshKeyMut = CborMap<RewardAccount, ulong>.Create([new(account, 5UL)]);
        account.Raw = new byte[] { 0xF6 };
        CborValidationException mutatedKeyErr = Assert.Throws<CborValidationException>(() =>
            CborSerializer.Serialize(freshKeyMut));
        Assert.Equal((-1, "CborMap", "non-null map key", "null"),
            (mutatedKeyErr.Offset, mutatedKeyErr.TypePath, mutatedKeyErr.Expected, mutatedKeyErr.Actual));

        RewardAccount valueAccount = new(Convert.FromHexString("E0" + new string('2', 56)));
        CborMap<int, RewardAccount> freshValMut = CborMap<int, RewardAccount>.Create([new(1, valueAccount)]);
        valueAccount.Raw = new byte[] { 0xF6 };
        CborValidationException mutatedValErr = Assert.Throws<CborValidationException>(() =>
            CborSerializer.Serialize(freshValMut));
        Assert.Equal((-1, "CborMap", "non-null map value", "null"),
            (mutatedValErr.Offset, mutatedValErr.TypePath, mutatedValErr.Expected, mutatedValErr.Actual));

        byte[] wireNull = Convert.FromHexString("A101F6");
        Assert.Equal(wireNull, CborSerializer.Serialize(CborMap<int, PlutusBoundedBytes>.Read(wireNull)));
    }

    [Fact]
    public void CborAnyDispatchSupportsMapListAndStandaloneRoundTrip()
    {
        byte[] oneEntry = Convert.FromHexString("A10001");
        CborMap<int, CborAny> map = CborMap<int, CborAny>.Read(oneEntry);
        Assert.Equal(new byte[] { 0x01 }, map[0].Raw.ToArray());
        Assert.True(map.TryGetValue(0, out CborAny? tryVal) && tryVal.Raw.Span.SequenceEqual(new byte[] { 0x01 }));
        foreach ((int k, CborAny v) in map)
        {
            Assert.Equal(0, k);
            Assert.Equal(new byte[] { 0x01 }, v.Raw.ToArray());
        }
        IEnumerable<KeyValuePair<int, CborAny>> boxed = map;
        Assert.Equal(new byte[] { 0x01 }, Assert.Single(boxed).Value.Raw.ToArray());
        Assert.Equal(new byte[] { 0x01 }, map.ToDictionary()[0].Raw.ToArray());

        byte[] multiWithTrailer = Convert.FromHexString("A20001028203047F");
        CborMap<int, CborAny> multi = CborMap<int, CborAny>.Read(multiWithTrailer, out int consumed);
        Assert.Equal(7, consumed);
        Assert.Equal(0x7F, multiWithTrailer[consumed]);
        Assert.Equal(new byte[] { 0x01 }, multi[0].Raw.ToArray());
        Assert.Equal(Convert.FromHexString("820304"), multi[2].Raw.ToArray());

        CborAny standalone = CborSerializer.Deserialize<CborAny>(Convert.FromHexString("0102"), out int anyConsumed);
        Assert.Equal(1, anyConsumed);
        Assert.Equal(new byte[] { 0x01 }, standalone.Raw.ToArray());
        Assert.Equal(new byte[] { 0x01 }, CborSerializer.Deserialize<CborAny>(Convert.FromHexString("0102")).Raw.ToArray());
        Type anyType = typeof(CborAny);
        Assert.Equal(new byte[] { 0x01 }, Assert.IsType<CborAny>(GenericSerializationUtil.Read(Convert.FromHexString("0102"), anyType)).Raw.ToArray());
        Assert.Equal(new byte[] { 0x01 }, Assert.IsType<CborAny>(GenericSerializationUtil.ReadAnyWithConsumed(Convert.FromHexString("0102"), anyType, out int typeConsumed)).Raw.ToArray());
        Assert.Equal(1, typeConsumed);

        CborMap<int, CborAny> freshMap = CborMap<int, CborAny>.Create([new(0, CborAny.FromRaw(new byte[] { 0x01 }))]);
        byte[] freshMapBytes = CborSerializer.Serialize(freshMap);
        Assert.Equal(oneEntry, freshMapBytes);
        Assert.Equal(new byte[] { 0x01 }, CborMap<int, CborAny>.Read(freshMapBytes)[0].Raw.ToArray());

        CborList<CborAny> freshList = CborList.Create([CborAny.FromRaw(new byte[] { 0x01 }), CborAny.FromRaw(Convert.FromHexString("820304"))]);
        byte[] freshListBytes = CborSerializer.Serialize(freshList);
        Assert.Equal(Convert.FromHexString("8201820304"), freshListBytes);
        CborList<CborAny> decodedList = CborList<CborAny>.Read(freshListBytes);
        Assert.Equal(2, decodedList.Count);
        CborAny[] listItems = [.. decodedList];
        Assert.Equal(new byte[] { 0x01 }, listItems[0].Raw.ToArray());
        Assert.Equal(Convert.FromHexString("820304"), listItems[1].Raw.ToArray());

        Assert.Equal(1L, CborSerializer.GetEncodedLength(CborAny.FromRaw(new byte[] { 0x01 })));

        foreach (CborAny bad in new[]
        {
            new CborAny(default),
            CborAny.FromRaw(new byte[] { 0x01, 0x02 }),
            CborAny.FromRaw(new byte[] { 0x42, 0xAA })
        })
        {
            _ = Assert.Throws<CborValidationException>(() =>
                CborSerializer.Serialize(CborMap<int, CborAny>.Create([new(0, bad)])));
            _ = Assert.Throws<CborValidationException>(() =>
                CborSerializer.Serialize(CborList.Create([bad])));
        }
        _ = Assert.Throws<CborValidationException>(() => CborSerializer.Serialize(new CborAny(default)));
    }

    [Fact]
    public void CborAnyWireNullPolicyMatchesCborMapContract()
    {
        byte[] wireNull = Convert.FromHexString("A100F6");
        CborMap<int, CborAny> decodedNull = CborMap<int, CborAny>.Read(wireNull);
        Assert.Equal(1, decodedNull.Count);
        Assert.True(decodedNull.ContainsKey(0));
        Assert.Equal(wireNull, CborSerializer.Serialize(decodedNull));
        AssertWireNullException(() => _ = decodedNull[0], 2, CborError.Expected(typeof(CborAny)));

        CborValidationException createErr = Assert.Throws<CborValidationException>(() =>
            CborMap<int, CborAny>.Create([new(0, CborAny.FromRaw(new byte[] { 0xF6 }))]));
        Assert.Equal((-1, "CborMap", "non-null map value", "null"),
            (createErr.Offset, createErr.TypePath, createErr.Expected, createErr.Actual));

        CborAny mutable = CborAny.FromRaw(new byte[] { 0x01 });
        CborMap<int, CborAny> mutatedMap = CborMap<int, CborAny>.Create([new(0, mutable)]);
        mutable.Raw = new byte[] { 0xF6 };
        CborValidationException writeErr = Assert.Throws<CborValidationException>(() =>
            CborSerializer.Serialize(mutatedMap));
        Assert.Equal((-1, "CborMap", "non-null map value", "null"),
            (writeErr.Offset, writeErr.TypePath, writeErr.Expected, writeErr.Actual));
    }

    [Fact]
    public void CborAnyRejectsBareBreakByteOnRead()
    {
        byte[] breakByte = [0xFF];
        Type anyType = typeof(CborAny);
        string anyTypePath = anyType.FullName!;
        Action[] readBreakCases =
        [
            () => _ = CborSerializer.Deserialize<CborAny>(breakByte),
            () => _ = CborSerializer.Deserialize<CborAny>(breakByte, out _),
            () => _ = GenericSerializationUtil.Read(breakByte, anyType),
            () => _ = GenericSerializationUtil.ReadAnyWithConsumed(breakByte, anyType, out _)
        ];
        foreach (Action readBreak in readBreakCases)
        {
            CborDecodeException err = Assert.Throws<CborDecodeException>(readBreak);
            Assert.Equal((0, anyTypePath, "valid CBOR data item", "unexpected break"),
                (err.Offset, err.TypePath, err.Expected, err.Actual));
        }
    }

    [Fact]
    public void CborAnyRejectsBareBreakByteOnWrite()
    {
        byte[] breakByte = [0xFF];
        byte[] oneByte = [0x01];
        Type anyType = typeof(CborAny);
        string anyTypePath = anyType.FullName!;
        CborAny breakAny = CborAny.FromRaw(breakByte);
        Action[] writeBreakCases =
        [
            () => GenericSerializationUtil.Write(new ArrayBufferWriter<byte>(), breakAny),
            () => _ = CborSerializer.Serialize(CborMap<int, CborAny>.Create([new(0, breakAny)], isIndefinite: false)),
            () => _ = CborSerializer.Serialize(CborMap<int, CborAny>.Create([new(0, breakAny)], isIndefinite: true)),
            () => _ = CborSerializer.Serialize(CborMap<CborAny, int>.Create([new(breakAny, 0)])),
            () => _ = CborSerializer.Serialize(CborList.Create([breakAny], isIndefinite: false)),
            () => _ = CborSerializer.Serialize(CborList.Create([breakAny, CborAny.FromRaw(oneByte)], isIndefinite: true))
        ];
        foreach (Action writeBreak in writeBreakCases)
        {
            CborValidationException err = Assert.Throws<CborValidationException>(writeBreak);
            Assert.Equal((-1, anyTypePath, "valid CBOR data item", "unexpected break"),
                (err.Offset, err.TypePath, err.Expected, err.Actual));
        }
    }

    [Fact]
    public void CborAnyPreservesValidIndefiniteContainersInMapAndList()
    {
        byte[] indefArrBytes = Convert.FromHexString("9F01FF");
        byte[] indefMapBytes = Convert.FromHexString("BF0102FF");
        CborAny indefArrAny = CborAny.FromRaw(indefArrBytes);
        CborAny indefMapAny = CborAny.FromRaw(indefMapBytes);

        Assert.Equal(indefArrBytes, CborSerializer.Deserialize<CborAny>(indefArrBytes).Raw.ToArray());
        Assert.Equal(indefMapBytes, CborSerializer.Deserialize<CborAny>(indefMapBytes).Raw.ToArray());

        ArrayBufferWriter<byte> directBuf = new();
        GenericSerializationUtil.Write(directBuf, indefArrAny);
        Assert.Equal(indefArrBytes, directBuf.WrittenSpan.ToArray());

        CborMap<int, CborAny> map = CborMap<int, CborAny>.Create([new(0, indefArrAny), new(1, indefMapAny)]);
        byte[] mapBytes = CborSerializer.Serialize(map);
        Assert.Equal(Convert.FromHexString("A2009F01FF01BF0102FF"), mapBytes);
        CborMap<int, CborAny> decodedMap = CborMap<int, CborAny>.Read(mapBytes);
        Assert.Equal(2, decodedMap.Count);
        Assert.Equal(indefArrBytes, decodedMap[0].Raw.ToArray());
        Assert.Equal(indefMapBytes, decodedMap[1].Raw.ToArray());

        CborList<CborAny> list = CborList.Create([indefArrAny, indefMapAny], isIndefinite: true);
        byte[] listBytes = CborSerializer.Serialize(list);
        Assert.Equal(Convert.FromHexString("9F9F01FFBF0102FFFF"), listBytes);
        CborList<CborAny> decodedList = CborList<CborAny>.Read(listBytes);
        Assert.Equal(2, decodedList.Count);
        CborAny[] items = [.. decodedList];
        Assert.Equal(indefArrBytes, items[0].Raw.ToArray());
        Assert.Equal(indefMapBytes, items[1].Raw.ToArray());
    }
}

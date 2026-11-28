using System.Buffers;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Chrysalis.Codec.Test;
using Chrysalis.Plutus.Builtins;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;
using Org.BouncyCastle.Crypto.Digests;
using Xunit;
using static Chrysalis.Plutus.Types.DefaultFunction;

namespace Chrysalis.Plutus.Test;

public sealed class BoundsTests
{
    private static readonly BigInteger Pow2_63 = BigInteger.One << 63;
    private static readonly PairType PairData = new(ConstantType.PlutusData, ConstantType.PlutusData);

    public static TheoryData<string, bool, Term<DeBruijn>, bool> CheckOrderCases() => new()
    {
        { "sha2_256 65537 B (E)", true, App(Sha2_256, Bytes(65_537)), true },
        { "consByteString 256 (E)", true, App(ConsByteString, Int(256), Bytes(0)), true },
        { "consByteString 256 (C)", false, App(ConsByteString, Int(256), Bytes(0)), true },
        { "indexByteString 2^63", false, App(IndexByteString, Bytes(1), Int(Pow2_63)), true },
        { "readBit 2^63", false, App(ReadBit, Bytes(1), Int(Pow2_63)), true },
        { "indexArray 2^63 (E)", true, App(IndexArray, new ArrayConstant(ConstantType.PlutusInteger, [Int(0)]), Int(Pow2_63)), true },
        { "replicateByte 256", false, App(ReplicateByte, Int(1), Int(256)), true },
        { "sliceByteString 2^63", false, App(SliceByteString, Int(Pow2_63), Int(0), Bytes(0)), true },
        { "shiftByteString 2^63 (E)", true, App(ShiftByteString, Bytes(1), Int(Pow2_63)), true },
        { "constrData wrong list (C)", false, App(ConstrData, Int(0), List(ConstantType.PlutusInteger)), true },
        { "mapData wrong list (E)", true, App(MapData, List(ConstantType.PlutusData)), true },
        { "listData wrong list (C)", false, App(ListData, List(PairData)), true },
        { "writeBits wrong list (E)", true, App(WriteBits, Bytes(1), List(ConstantType.PlutusBool), BoolConstant.False), true },
        { "G1_multiScalarMul arg1 wrong list (C)", false, App(Bls12_381_G1_MultiScalarMul, List(ConstantType.PlutusInteger), List(ConstantType.Bls12381G2Element)), true },
        { "G2_multiScalarMul arg0 wrong list (E)", true, App(Bls12_381_G2_MultiScalarMul, List(ConstantType.PlutusBool), List(ConstantType.Bls12381G2Element)), true },
        { "listData non-empty wrong list (E)", true, App(ListData, List(ConstantType.PlutusInteger, Int(1))), true },
        { "G1_multiScalarMul arg0 wrong list (E)", true, App(Bls12_381_G1_MultiScalarMul, List(ConstantType.PlutusBool), List(ConstantType.Bls12381G1Element)), true },
        { "G2_multiScalarMul arg1 wrong list (C)", false, App(Bls12_381_G2_MultiScalarMul, List(ConstantType.PlutusInteger), List(ConstantType.Bls12381G1Element)), true },
        { "multiIndexArray indices of bool (C)", false, App(MultiIndexArray, new ArrayConstant(ConstantType.PlutusInteger, [Int(10)]), List(ConstantType.PlutusBool)), true },
        { "keepPolicies integer ids (C)", false, App(KeepPolicies, List(ConstantType.PlutusInteger), new ValueConstant(new LedgerValue([]))), true },
        { "dropPolicies non-empty integer ids (C)", false, App(DropPolicies, List(ConstantType.PlutusInteger, Int(1)), new ValueConstant(new LedgerValue([]))), true },
        { "writeBits 4097 B (E)", true, App(WriteBits, Bytes(4097), List(ConstantType.PlutusInteger), BoolConstant.False), false },
        { "sha2_256 65537 B (C)", false, App(Sha2_256, Bytes(65_537)), false },
        { "indexByteString 5 (C)", false, App(IndexByteString, Bytes(1), Int(5)), false },
        { "multiIndexArray index out of range (C)", false, App(MultiIndexArray, new ArrayConstant(ConstantType.PlutusInteger, [Int(10)]), List(ConstantType.PlutusInteger, Int(1))), false },
        { "multiIndexArray not an array (C)", false, App(MultiIndexArray, Int(0), List(ConstantType.PlutusInteger)), false }
    };

    [Theory]
    [MemberData(nameof(CheckOrderCases))]
    public void BoundsAreCheckedWhereUpstreamChecksThem(string name, bool useLatestV3, Term<DeBruijn> term, bool throwsBeforeCharge)
    {
        _ = name;
        CostModel model = useLatestV3 ? CostModel.LatestV3 : CostModel.Default;
        ExBudget margin = new(1, 1);
        CekMachine machine = new(model.Machine.Startup + margin, model);
        if (throwsBeforeCharge)
        {
            _ = Assert.Throws<EvaluationException>(() => machine.TryRun(term, out _));
            Assert.Equal(BudgetStop.None, machine.StoppedAt);
            Assert.Equal(margin, machine.RemainingBudget);
        }
        else
        {
            Assert.False(machine.TryRun(term, out _));
            Assert.Equal(BudgetStop.Builtin, machine.StoppedAt);
        }
    }

    [Fact]
    public void BoundsCheckAllocatesNothing()
    {
        // Distinct PairType instance from BuiltinBounds' private static one to verify structural equality allocates nothing.
        PairType distinctPairData = new(ConstantType.PlutusData, ConstantType.PlutusData);
        (DefaultFunction Func, CekValue[] Args, Variant Variant)[] cases =
        [
            (AddInteger, [Val(Int(1)), Val(Int(2))], Variant.E),
            (Sha2_256, [Val(Bytes(64))], Variant.E),
            (SliceByteString, [Val(Int(0)), Val(Int(4)), Val(Bytes(64))], Variant.E),
            (ConstrData, [Val(Int(0)), Val(List(ConstantType.PlutusData))], Variant.E),
            (ConsByteString, [Val(Int(42)), Val(Bytes(4))], Variant.C),
            (WriteBits, [Val(Bytes(64)), Val(List(ConstantType.PlutusInteger)), Val(BoolConstant.True)], Variant.E),
            (Bls12_381_G1_ScalarMul, [Val(Int(7)), Val(new Bls12381G1Constant(new byte[48]))], Variant.E),
            (MapData, [Val(List(distinctPairData))], Variant.E),
            (Bls12_381_G1_MultiScalarMul, [Val(List(ConstantType.PlutusInteger)), Val(List(ConstantType.Bls12381G1Element))], Variant.E)
        ];

        int count = 0;
        long allocated = AllocationAssert.Measure(
            () => RunAll(cases),
            () =>
            {
                count = 0;
                for (int i = 0; i < 1000; i++)
                {
                    RunAll(cases);
                    count++;
                }
            },
            () => Assert.Equal(1000, count));
        Assert.Equal(0, allocated);

        static void RunAll((DefaultFunction Func, CekValue[] Args, Variant Variant)[] items)
        {
            foreach ((DefaultFunction func, CekValue[] args, Variant variant) in items)
            {
                BuiltinBounds.CheckArguments(func, args, variant);
                BuiltinBounds.CheckCharged(func, args, variant);
            }
        }
    }

    // Value.hs:585-598: valueData fails in the denotation, so after the machine's charge.
    public static TheoryData<int, int, bool> ValueDataCases() => new()
    {
        { 20_000, 20_000, false },
        { 20_000, 20_001, false },
        { 20_000, 20_001, true }
    };

    [Theory]
    [MemberData(nameof(ValueDataCases))]
    public void ValueDataStopsAtTheNodesSize(int first, int second, bool charged)
    {
        CostModel model = CostModel.LatestV3;
        Term<DeBruijn> term = App(ValueData, new ValueConstant(new LedgerValue([TokenCurrency(0, first), TokenCurrency(1, second)])));
        ExBudget? budget = charged ? model.Machine.Startup + new ExBudget(1, 1) : null;
        CekMachine machine = new(budget, model);
        if (charged)
        {
            Assert.False(machine.TryRun(term, out _));
            Assert.Equal(BudgetStop.Builtin, machine.StoppedAt);
        }
        else if (second > 20_000)
        {
            _ = Assert.Throws<EvaluationException>(() => machine.TryRun(term, out _));
        }
        else
        {
            Assert.True(machine.TryRun(term, out Term<DeBruijn>? result));
            PlutusDataMap map = Assert.IsType<PlutusDataMap>(Assert.IsType<DataConstant>(Assert.IsType<ConstTerm<DeBruijn>>(result).Value).Value);
            Assert.Equal(2, map.Entries.Length);
            Assert.All(map.Entries, entry => Assert.Equal(20_000, Assert.IsType<PlutusDataMap>(entry.Value).Entries.Length));
        }
    }

    private static CurrencyEntry TokenCurrency(byte key, int count) => new(
        new byte[] { key }, [.. Enumerable.Range(0, count).Select(i => new TokenEntry(new[] { (byte)(i >> 16), (byte)(i >> 8), (byte)i }, 1))]);

    [Fact]
    public void SliceByteStringReturnsAView()
    {
        byte[] source = [0, 1, 2, 3, 4, 5];
        CekValue sliced = BuiltinRuntime.Call(SliceByteString, [Val(Int(2)), Val(Int(3)), Val(new ByteStringConstant(source))]);
        ReadOnlyMemory<byte> slice = Assert.IsType<ByteStringConstant>(Assert.IsType<VConstant>(sliced).Value).Value;
        Assert.True(MemoryMarshal.TryGetArray(slice, out ArraySegment<byte> segment));
        Assert.Same(source, segment.Array);
        Assert.Equal((2, 3), (segment.Offset, segment.Count));
    }

    // The reference runs the same digest over the same bytes, so what is left is the builtin's own objects, and at the seams the input copy.
    public static TheoryData<DefaultFunction, Func<Org.BouncyCastle.Crypto.IDigest>> Hashes() => new()
    {
        { Sha2_256, () => new Sha256Digest() },
        { Sha3_256, () => new Sha3Digest(256) },
        { Blake2b_256, () => new Blake2bDigest(256) },
        { Blake2b_224, () => new Blake2bDigest(224) },
        { Keccak_256, () => new KeccakDigest(256) },
        { Ripemd_160, () => new RipeMD160Digest() }
    };

    [Theory]
    [MemberData(nameof(Hashes))]
    public void HashBuiltinsReadTheirInputInPlace(DefaultFunction function, Func<Org.BouncyCastle.Crypto.IDigest> newDigest)
    {
        ArgumentNullException.ThrowIfNull(newDigest);
        byte[] input = new byte[4096];
        CekValue[] args = [Val(new ByteStringConstant(input))];
        Org.BouncyCastle.Crypto.IDigest digest = newDigest();
        byte[] output = new byte[digest.GetDigestSize()];
        long builtin = AllocationAssert.Measure(() => _ = BuiltinRuntime.Call(function, args), () => _ = BuiltinRuntime.Call(function, args));
        long reference = AllocationAssert.Measure(Hash, Hash);
        Assert.True(builtin - reference < 4096, $"{function}: {builtin} B against the digest's own {reference} B");

        void Hash()
        {
            digest.BlockUpdate(input);
            _ = digest.DoFinal(output);
        }
    }

    // Value.hs:307-311: the total size is a 64-bit Int.
    [Fact]
    public void ValueSizeCountsPastInt32()
    {
        ImmutableArray<TokenEntry> tokens = [.. Enumerable.Repeat(new TokenEntry(new byte[1], 1), 65_536)];
        CurrencyEntry currency = new(new byte[1], tokens);
        Assert.Equal(2_147_549_184L, ExMem.ValueSizeExMem(new LedgerValue([.. Enumerable.Repeat(currency, 32_769)])));
    }

    private sealed class CountingMemory(byte[] bytes) : MemoryManager<byte>
    {
        internal int Reads { get; set; }

        public override Span<byte> GetSpan()
        {
            Reads++;
            return bytes;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin() { }

        protected override void Dispose(bool disposing) { }
    }

    private static ByteStringConstant Sought(int i) => new(new[] { (byte)(i >> 8), (byte)i });

    // Value.hs:380-457, 532-543 and ExMemoryUsage.hs:453-481: the node searches two maps, keeps the counts its cost reads, and answers valueContains from them.
    // Every key of the counted values is a slice of one memory whose span counts its reads, so one read is one comparison.
    [Fact]
    public void CoinBuiltinsSearchTheValue()
    {
        byte[] keys = new byte[2 * 16_385];
        for (int i = 0; i <= 16_384; i++)
        {
            keys[2 * i] = (byte)(i >> 8);
            keys[(2 * i) + 1] = (byte)i;
        }
        using CountingMemory counting = new(keys);
        ImmutableArray<TokenEntry> single = [new TokenEntry(Key(0), 1)];
        ImmutableArray<TokenEntry> largest = [.. Enumerable.Range(0, 16_384).Select(i => new TokenEntry(Key(i), 1))];
        LedgerValue v = new([.. Enumerable.Range(0, 16_383).Select(i => new CurrencyEntry(Key(i), single)), new CurrencyEntry(Key(16_383), largest)]);
        LedgerValue w = new([new CurrencyEntry(Sought(16_383).Value, [new TokenEntry(Sought(16_383).Value, 1)])]);
        LedgerValue n = new([new CurrencyEntry(Sought(0).Value, [new TokenEntry(Sought(0).Value, -1)])]);
        LedgerValue u = new([.. Enumerable.Range(0, 1_024).Select(i => new CurrencyEntry(Key(i), single))]);
        LedgerValue uPlus = new([.. u.Entries, new CurrencyEntry(Key(1_024), single)]);
        LedgerValue small = new([.. v.Entries.Take(16)]);
        ListConstant present = List(ConstantType.PlutusByteString, Sought(0));
        CekValue[] absent = [Val(List(ConstantType.PlutusByteString, Sought(16_384))), Val(new ValueConstant(v))];
        long lookup = 219_951 + (9_444 * 30);
        long insert = 356_924 + (18_413 * 30);
        long contains = 618_401 + (1_998 * 32_767) + 28_258;
        LedgerValue r6 = v;
        LedgerValue r9 = v;

        ReadOnlyMemory<byte> Key(int i) => counting.Memory.Slice(2 * i, 2);
        Term<DeBruijn> Plain(LedgerValue value) => new ConstTerm<DeBruijn>(new ValueConstant(value));
        Term<DeBruijn> Inserting(int quantity, Term<DeBruijn> value) => new ApplyTerm<DeBruijn>(App(InsertCoin, Sought(16_383), Sought(16_383), Int(quantity)), value);
        Term<DeBruijn> Containing(Term<DeBruijn> first, LedgerValue second) => App(ValueContains, first, Plain(second));
        BigInteger Quantity(CekValue result) => Assert.IsType<IntegerConstant>(Assert.IsType<VConstant>(result).Value).Value;

        (long Cpu, int Reads, CekValue Result) Run(Term<DeBruijn> term, DefaultFunction function)
        {
            counting.Reads = 0;
            (_, long cpu, _, CekValue result) = NodeBudgetTests.WithCounters(term, CostModel.LatestV3, function);
            return (cpu, counting.Reads, result);
        }

        void Lookup(int currency, int token, LedgerValue value, int quantity, int maxReads, long charge)
        {
            (long cpu, int reads, CekValue result) = Run(App(LookupCoin, Sought(currency), Sought(token), new ValueConstant(value)), LookupCoin);
            Assert.Equal((charge, (BigInteger)quantity), (cpu, Quantity(result)));
            Assert.InRange(reads, 0, maxReads);
        }

        LedgerValue Insert(int currency, int token, int quantity, LedgerValue value, int maxReads, (int, long, long) kept)
        {
            (long cpu, int reads, CekValue result) = Run(App(InsertCoin, Sought(currency), Sought(token), Int(quantity), new ValueConstant(value)), InsertCoin);
            LedgerValue changed = Assert.IsType<ValueConstant>(Assert.IsType<VConstant>(result).Value).Value;
            Assert.Equal((insert, kept), (cpu, changed.Kept));
            Assert.InRange(reads, 0, maxReads);
            CekValue stored = ValueBuiltins.LookupCoin([Val(Sought(currency)), Val(Sought(token)), Val(new ValueConstant(changed))]);
            Assert.Equal(quantity, Quantity(stored));
            return changed;
        }

        void Verdict(Term<DeBruijn> term, bool expected, int maxReads, long charge)
        {
            (long cpu, int reads, CekValue result) = Run(term, ValueContains);
            Assert.Equal((charge, expected), (cpu, Assert.IsType<BoolConstant>(Assert.IsType<VConstant>(result).Value).Value));
            Assert.InRange(reads, 0, maxReads);
        }

        // keepPolicies and dropPolicies of one id, searched once in the outer map.
        LedgerValue Filtered(DefaultFunction function, int currency)
        {
            (_, int reads, CekValue result) = Run(App(function, List(ConstantType.PlutusByteString, Sought(currency)), new ValueConstant(v)), function);
            Assert.InRange(reads, 0, 15);
            return Assert.IsType<ValueConstant>(Assert.IsType<VConstant>(result).Value).Value;
        }

        void Fails(Term<DeBruijn> term, string position) =>
            Assert.Equal($"valueContains: negative quantity in {position} value", Assert.Throws<EvaluationException>(() => Run(term, ValueContains)).Message);

        (string Name, Action Check)[] rows =
        [
            ("1 lookupCoin, a hit", () => Lookup(16_383, 16_383, v, 1, 30, lookup)),
            ("2 lookupCoin, a miss", () => Lookup(16_384, 0, v, 0, 15, lookup)),
            ("3 a new token", () => _ = Insert(16_383, 16_384, 1, v, 30, (16_385, 32_768, 0))),
            ("4 a new currency", () => _ = Insert(16_384, 0, 1, v, 15, (16_384, 32_768, 0))),
            ("5 a replaced token, the same sign", () => _ = Insert(16_383, 5, 2, v, 30, (16_384, 32_767, 0))),
            ("6 plus to minus", () => r6 = Insert(16_383, 5, -2, v, 30, (16_384, 32_767, 1))),
            ("7 minus to plus", () => _ = Insert(16_383, 5, 2, r6, 30, (16_384, 32_767, 0))),
            ("8 a new negative token", () => _ = Insert(16_383, 16_384, -2, v, 30, (16_385, 32_768, 1))),
            ("9 plus to minus, the first currency", () => r9 = Insert(0, 0, -2, v, 16, (16_384, 32_767, 1))),
            ("10 a negative and its currency deleted", () => Assert.Equal(16_383, Insert(0, 0, 0, r9, 16, (16_384, 32_766, 0)).Entries.Length)),
            ("11 a deletion from the largest list", () => Lookup(16_383, 5, Insert(16_383, 5, 0, v, 30, (-1, 32_766, 0)), 0, 30, 219_951 + (9_444 * 29))),
            ("12 nothing to delete", () => Assert.Same(v, Insert(16_383, 16_384, 0, v, 30, (16_384, 32_767, 0)))),
            ("13 valueContains, a True", () => Verdict(Containing(Plain(v), w), true, 30, contains)),
            ("14 a negative made by insertCoin", () => Fails(Containing(Inserting(-2, Plain(v)), w), "first")),
            ("15 and made positive again", () => Verdict(Containing(Inserting(2, Inserting(-2, Plain(v))), w), true, int.MaxValue, contains)),
            ("16 the early False", () => Verdict(Containing(Plain(u), uPlus), false, 0, 213_283)),
            ("17 the negative check comes first", () => Fails(Containing(Plain(n), u), "first")),
            ("18 insertCoin copies each level once", () => Assert.InRange(Allocated(v, InsertCoin, Sought(16_383), Sought(16_384), Int(1)), 0, (8 * (16_384 + 16_384)) + 1024)),
            ("19 lookupCoin allocates nothing", () => Assert.Equal(0, Allocated(v, LookupCoin, Sought(16_383), Sought(16_383)))),
            ("20 a with copy starts with no counts", () =>
            {
                LedgerValue copy = v with { };
                Assert.Equal((-1, -1L, -1L), copy.Kept);
                Verdict(Containing(Plain(copy), w), true, 30, contains);
                Assert.Equal((16_384, 32_767L, 0L), copy.Kept);
            }),
            ("21 and one with a negative still fails", () =>
            {
                LedgerValue copy = n with { };
                Assert.Equal((-1, -1L, -1L), copy.Kept);
                Fails(Containing(Plain(copy), u), "first");
            }),
            ("22 keepPolicies allocates the same on 16 and 16,384 currencies", () =>
            {
                long bytes = Allocated(v, KeepPolicies, present);
                Assert.Equal(Allocated(small, KeepPolicies, present), bytes);
                Assert.InRange(bytes, 1, 1024);
            }),
            ("23 keepPolicies keeps the one hit", () => Assert.Equal(Sought(5).Value.ToArray(), Assert.Single(Filtered(KeepPolicies, 5).Entries).Currency.ToArray())),
            ("24 dropPolicies removes the one hit", () => Assert.Equal(16_383, Filtered(DropPolicies, 5).Entries.Length)),
            ("25 dropPolicies of an absent id returns its Value", () => Assert.Same(absent[1], BuiltinRuntime.Call(DropPolicies, absent))),
            ("26 assetCount reads no key", () =>
            {
                (_, int reads, CekValue result) = Run(App(AssetCount, new ValueConstant(v)), AssetCount);
                Assert.Equal((0, (BigInteger)32_767), (reads, Quantity(result)));
            })
        ];
        Assert.All(rows, row =>
        {
            Exception? failure = Record.Exception(row.Check);
            Assert.True(failure is null, $"row {row.Name}: {failure?.Message}");
        });

        long Allocated(LedgerValue value, DefaultFunction function, params Constant[] args)
        {
            CekValue[] values = [.. args.Select(Val), Val(new ValueConstant(value))];
            return AllocationAssert.Measure(() => _ = BuiltinRuntime.Call(function, values), () => _ = BuiltinRuntime.Call(function, values));
        }
    }

    private static Term<DeBruijn> App(DefaultFunction func, params Constant[] args) =>
        App(func, [.. args.Select(arg => new ConstTerm<DeBruijn>(arg))]);

    private static Term<DeBruijn> App(DefaultFunction func, params Term<DeBruijn>[] args)
    {
        Term<DeBruijn> term = new BuiltinTerm<DeBruijn>(func);
        for (int i = 0; i < func.ForceCount(); i++)
        {
            term = new ForceTerm<DeBruijn>(term);
        }
        foreach (Term<DeBruijn> arg in args)
        {
            term = new ApplyTerm<DeBruijn>(term, arg);
        }
        return term;
    }

    private static VConstant Val(Constant c) => new(c);
    private static IntegerConstant Int(BigInteger n) => new(n);
    private static ByteStringConstant Bytes(int length) => new(new byte[length]);
    private static ListConstant List(ConstantType itemType, params Constant[] items) => new(itemType, [.. items]);
}

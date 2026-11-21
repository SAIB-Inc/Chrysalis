using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Test;
using Chrysalis.Plutus.Builtins;
using Chrysalis.Plutus.Cbor;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Plutus.VM.Models;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed class DeepNestingTests
{
    private const int DeepLevels = 1_000_000;

    private sealed record LimitRow(string Id, string? Shape, int N, string? Body, byte[]? Flat, string FlatSha256, ExUnitsResult ExUnits);

    private sealed record LimitsData(long[] V3Model, Dictionary<string, LimitRow> ById, string[] Ids);

    private static readonly Lazy<LimitsData> DataLimits = new(LoadLimits);

    private static LimitsData LoadLimits()
    {
        using GZipStream stream = new(Golden.Open("plutigo-v0.12.3/node_data_limits_pv11.json.gz"), CompressionMode.Decompress);
        using JsonDocument doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;
        Assert.Equal(11, root.GetProperty("protocolMajor").GetInt32());
        long[] v3 = [.. root.GetProperty("costModels").GetProperty("PlutusV3").EnumerateArray().Select(x => x.GetInt64())];
        Assert.Equal(NodeBudgetTests.Fixture.Models[2], v3);
        Dictionary<string, LimitRow> byId = [];
        List<string> ids = [];
        foreach (JsonElement entry in root.GetProperty("scripts").EnumerateArray())
        {
            Assert.Equal("V3", entry.GetProperty("lang").GetString());
            Assert.Equal("ok", entry.GetProperty("outcome").GetString());
            string id = entry.GetProperty("id").GetString()!;
            byte[]? flat = entry.TryGetProperty("flat", out JsonElement fl) && fl.GetString() is { Length: > 0 } hex
                ? Convert.FromHexString(hex)
                : null;
            JsonElement units = entry.GetProperty("exunits");
            ExUnitsResult ex = new(units.GetProperty("mem").GetUInt64(), units.GetProperty("steps").GetUInt64());
            byId.Add(id, new(
                id,
                entry.TryGetProperty("shape", out JsonElement sh) ? sh.GetString() : null,
                entry.TryGetProperty("n", out JsonElement nv) ? nv.GetInt32() : 0,
                entry.TryGetProperty("body", out JsonElement bd) ? bd.GetString() : null,
                flat,
                entry.GetProperty("flatSha256").GetString()!,
                ex));
            ids.Add(id);
        }

        Assert.Equal(15, ids.Count);
        return new(v3, byId, [.. ids]);
    }

    public static TheoryData<string> DataLimitIds() => [.. DataLimits.Value.Ids];

    [Theory]
    [MemberData(nameof(DataLimitIds))]
    public void DeepDataMatchesTheNode(string id)
    {
        LimitsData limits = DataLimits.Value;
        LimitRow row = limits.ById[id];
        byte[] flat = row.Flat ?? BuildProbe(row.Shape!, row.N, row.Body!);
        Assert.Equal(row.FlatSha256, Convert.ToHexStringLower(SHA256.HashData(flat)));
        EvaluationResult result = Evaluator.EvaluateScript(flat, [new PlutusDataConstr(0, [])], 3, 11, limits.V3Model);
        Assert.Equal(row.ExUnits, result.ExUnits);
    }

    [Fact]
    public void DeepDataReadsWithoutRecursion()
    {
        byte[] input = BuildDeepCbor(DeepLevels, nodeForm: false);
        Started(nameof(DeepDataReadsWithoutRecursion));
        PlutusData current = CborReader.DecodePlutusData(input);
        for (int i = 0; i < DeepLevels; i++)
        {
            current = (i % 3) switch
            {
                0 => current is PlutusDataList { Values.Length: 1 } l ? l.Values[0] : Fail<PlutusData>($"list mismatch at {i}"),
                1 => current is PlutusDataConstr { Fields.Length: 1 } c && c.Tag.IsZero ? c.Fields[0] : Fail<PlutusData>($"constr mismatch at {i}"),
                _ => current is PlutusDataMap { Entries.Length: 1 } m && m.Entries[0].Key is PlutusDataInteger { Value: var k } && k.IsZero
                    ? m.Entries[0].Value
                    : Fail<PlutusData>($"map mismatch at {i}")
            };
        }

        Assert.True(current is PlutusDataInteger { Value: var leaf } && leaf.IsZero, "Expected leaf integer 0.");
    }

    [Fact]
    public void DeepDataWritesWithoutRecursion()
    {
        PlutusData data = BuildDeepData(DeepLevels, leaf: 0);
        byte[] expected = BuildDeepCbor(DeepLevels, nodeForm: true);
        Started(nameof(DeepDataWritesWithoutRecursion));
        byte[] actual = CborWriter.EncodePlutusData(data);
        Assert.True(actual.AsSpan().SequenceEqual(expected), $"Encoded length {actual.Length}, expected {expected.Length}.");
    }

    [Fact]
    public void DeepDataComparesWithoutRecursion()
    {
        PlutusData a = BuildDeepData(DeepLevels, leaf: 0);
        PlutusData b = BuildDeepData(DeepLevels, leaf: 0);
        PlutusData c = BuildDeepData(DeepLevels, leaf: 1);
        Started(nameof(DeepDataComparesWithoutRecursion));
        CekValue eq = DataBuiltins.EqualsData([new VConstant(new DataConstant(a)), new VConstant(new DataConstant(b))]);
        CekValue ne = DataBuiltins.EqualsData([new VConstant(new DataConstant(a)), new VConstant(new DataConstant(c))]);
        Assert.True(eq is VConstant { Value: BoolConstant { Value: true } }, "Expected identical deep data to compare equal.");
        Assert.True(ne is VConstant { Value: BoolConstant { Value: false } }, "Expected differing deep leaf to compare unequal.");
    }

    [Fact]
    public void DeepTermDecodesWithoutRecursion()
    {
        byte[] flat = BuildDeepTermChain(DeepLevels);
        Started(nameof(DeepTermDecodesWithoutRecursion));
        Term<DeBruijn> current = FlatDecoder.DecodeProgram(flat).Term;
        for (int i = 0; i < DeepLevels; i++)
        {
            current = (i % 6) switch
            {
                0 => current is DelayTerm<DeBruijn> d ? d.Body : Fail<Term<DeBruijn>>($"delay mismatch at {i}"),
                1 => current is LambdaTerm<DeBruijn> l ? l.Body : Fail<Term<DeBruijn>>($"lambda mismatch at {i}"),
                2 => current is ForceTerm<DeBruijn> f ? f.Body : Fail<Term<DeBruijn>>($"force mismatch at {i}"),
                3 => current is ApplyTerm<DeBruijn> { Function: ConstTerm<DeBruijn> { Value: UnitConstant } } a ? a.Argument : Fail<Term<DeBruijn>>($"apply mismatch at {i}"),
                4 => current is ConstrTerm<DeBruijn> { Tag: 0, Fields.Length: 1 } c ? c.Fields[0] : Fail<Term<DeBruijn>>($"constr mismatch at {i}"),
                _ => current is CaseTerm<DeBruijn> { Branches.Length: 0 } cs ? cs.Scrutinee : Fail<Term<DeBruijn>>($"case mismatch at {i}")
            };
        }

        Assert.True(current is ConstTerm<DeBruijn> { Value: UnitConstant }, "Expected leaf unit constant.");
    }

    [Fact]
    public void DeepApplicationSpineChecksScopeWithoutRecursion()
    {
        ConstTerm<DeBruijn> unit = new(UnitConstant.Instance);
        Term<DeBruijn> closed = unit;
        Term<DeBruijn> open = new VarTerm<DeBruijn>(new(1));
        for (int i = 0; i < DeepLevels; i++)
        {
            closed = new ApplyTerm<DeBruijn>(closed, unit);
            open = new ApplyTerm<DeBruijn>(open, unit);
        }

        Started(nameof(DeepApplicationSpineChecksScopeWithoutRecursion));
        Assert.True(Evaluator.IsClosed(closed, 0), "Expected closed application spine.");
        Assert.False(Evaluator.IsClosed(open, 0), "Expected open application spine to report unbound.");
    }

    [Fact]
    public void CaseWith1025BranchesEvaluatesUnderPv11Limits()
    {
        EvaluationResult result = Evaluator.EvaluateScript(BuildCase1025(), [new PlutusDataConstr(0, [])], 3, 11, DataLimits.Value.V3Model);
        Assert.True(result.ExUnits.Steps > 0);
    }

    [Theory]
    [InlineData("v3-ignore-delay")]
    [InlineData("v3-ignore-left-spine")]
    [InlineData("v3-return-delay")]
    [InlineData("v1-return-delay")]
    public void DeepScriptsEvaluateAsOnTheNode(string kind)
    {
        byte[] flat = BuildDeepScript(kind, DeepLevels);
        PlutusDataConstr arg = new(0, []);
        if (kind == "v3-return-delay")
        {
            _ = Assert.Throws<InvalidReturnValueException>(() => Evaluator.EvaluateScript(flat, [arg], 3, 11, DataLimits.Value.V3Model));
            return;
        }

        EvaluationResult result = kind == "v1-return-delay"
            ? Evaluator.EvaluateScript(flat, [arg, arg], 1, 11, NodeBudgetTests.Fixture.Models[0])
            : Evaluator.EvaluateScript(flat, [arg], 3, 11, DataLimits.Value.V3Model);
        Assert.True(result.ExUnits.Steps > 0);
    }

    [Fact]
    public void DeepEqualsDataAllocatesNothing()
    {
        PlutusData a = BuildDeepData(1_000, leaf: 0);
        PlutusData b = BuildDeepData(1_000, leaf: 0);
        CekValue[] args = [new VConstant(new DataConstant(a)), new VConstant(new DataConstant(b))];
        CekValue? observed = null;
        long bytes = AllocationAssert.Measure(
            () => observed = DataBuiltins.EqualsData(args),
            () =>
            {
                for (int i = 0; i < 1_000; i++)
                {
                    observed = DataBuiltins.EqualsData(args);
                }
            },
            () => Assert.True(observed is VConstant { Value: BoolConstant { Value: true } }));
        Assert.Equal(0L, bytes);
    }

    private static byte[] BuildDeepTermChain(int levels)
    {
        FlatProbe p = new(1, 1, 0);
        for (int i = 0; i < levels; i++)
        {
            _ = (i % 6) switch
            {
                0 => p.Delay(),
                1 => p.Lam(),
                2 => p.Force(),
                3 => p.App().Unit(),
                4 => p.Constr(0).Bit(1),
                _ => p.Case()
            };
        }

        _ = p.Unit();
        for (int i = levels - 1; i >= 0; i--)
        {
            if ((i % 6) is 4 or 5)
            {
                _ = p.Bit(0);
            }
        }

        return p.Finish();
    }

    private static byte[] BuildCase1025()
    {
        FlatProbe p = new(1, 1, 0);
        _ = p.Lam().Case().Constr(0).Bit(0).Bit(1).Unit();
        for (int i = 0; i < 1024; i++)
        {
            _ = p.Bit(1).Err();
        }

        return p.Bit(0).Finish();
    }

    private static byte[] BuildDeepScript(string kind, int levels)
    {
        FlatProbe p = new(1, kind == "v1-return-delay" ? 0 : 1, 0);
        _ = p.Lam();
        if (kind is "v3-ignore-delay" or "v3-ignore-left-spine")
        {
            _ = p.App().Lam().Unit();
        }
        else if (kind == "v1-return-delay")
        {
            _ = p.Lam();
        }

        if (kind == "v3-ignore-left-spine")
        {
            _ = p.Delay();
            for (int i = 0; i < levels; i++)
            {
                _ = p.App();
            }

            for (int i = 0; i <= levels; i++)
            {
                _ = p.Unit();
            }
        }
        else
        {
            for (int i = 0; i < levels; i++)
            {
                _ = p.Delay();
            }

            _ = p.Unit();
        }

        return p.Finish();
    }

    private static void Started(string name)
    {
        if (System.Environment.GetEnvironmentVariable("V4B_WITNESS_DIR") is { Length: > 0 } dir)
        {
            File.WriteAllText(Path.Combine(dir, $"{name}.started"), name);
        }
    }

    private static T Fail<T>(string message)
    {
        Assert.Fail(message);
        return default!;
    }

    private static PlutusData BuildDeepData(int levels, int leaf)
    {
        PlutusData current = new PlutusDataInteger(leaf);
        PlutusData zero = new PlutusDataInteger(BigInteger.Zero);
        for (int i = levels - 1; i >= 0; i--)
        {
            current = (i % 3) switch
            {
                0 => new PlutusDataList([current], IsDefinite: true),
                1 => new PlutusDataConstr(BigInteger.Zero, [current], IsDefinite: true),
                _ => new PlutusDataMap([(zero, current)])
            };
        }

        return current;
    }

    private static byte[] BuildDeepCbor(int levels, bool nodeForm)
    {
        byte arrayHead = nodeForm ? (byte)0x9F : (byte)0x81;
        byte[] tail = (levels % 3) switch
        {
            1 => [arrayHead],
            2 => [arrayHead, 0xD8, 0x79, arrayHead],
            _ => []
        };
        byte[] breaks = nodeForm ? Repeat([0xFF], (2 * (levels / 3)) + Math.Min(levels % 3, 2)) : [];
        return [.. Repeat([arrayHead, 0xD8, 0x79, arrayHead, 0xA1, 0x00], levels / 3), .. tail, 0x00, .. breaks];
    }

    private static (byte[] Raw, byte[] Canonical) DataShape(string shape, int n) => shape switch
    {
        "deepL" => ([.. Repeat([0x81], n), 0x00], [.. Repeat([0x9F], n), 0x00, .. Repeat([0xFF], n)]),
        "deepC" => ([.. Repeat([0xD8, 0x79, 0x81], n), 0x00], [.. Repeat([0xD8, 0x79, 0x9F], n), 0x00, .. Repeat([0xFF], n)]),
        "wideL" => ([.. CborHead(4, n), .. Repeat([0x00], n)], [0x9F, .. Repeat([0x00], n), 0xFF]),
        "wideM" => ((byte[])[.. CborHead(5, n), .. Repeat([0x00, 0x00], n)]) is var map ? (map, map) : default,
        _ => Fail<(byte[], byte[])>($"Unknown shape {shape}")
    };

    private static byte[] CborHead(byte major, int count) => ((byte)(major << 5), count) switch
    {
        (byte p, < 24) => [(byte)(p | count)],
        (byte p, < 256) => [(byte)(p | 24), (byte)count],
        (byte p, < 65536) => [(byte)(p | 25), (byte)(count >> 8), (byte)count],
        (byte p, _) => [(byte)(p | 26), (byte)(count >> 24), (byte)(count >> 16), (byte)(count >> 8), (byte)count]
    };

    private static byte[] Repeat(byte[] unit, int count)
    {
        byte[] result = new byte[unit.Length * count];
        if (unit.Length == 1)
        {
            result.AsSpan().Fill(unit[0]);
            return result;
        }

        for (int i = 0; i < count; i++)
        {
            unit.CopyTo(result.AsSpan(i * unit.Length));
        }

        return result;
    }

    private static byte[] BuildProbe(string shape, int n, string body)
    {
        (byte[] raw, byte[] canonical) = DataShape(shape, n);
        FlatProbe p = new(1, 1, 0);
        void CheckLen(int len) => _ = p.App().App().Builtin(7).App().Builtin(13).Var(1).Int(len);
        switch (body)
        {
            case "ign":
                _ = p.Lam().App().Lam().Unit().Data(raw);
                break;
            case "eq":
                _ = p.Lam().App().Lam().AssertAll(() => _ = p.App().App().Builtin(47).Var(1).Var(1)).Data(raw);
                break;
            case "serlen":
                _ = p.Lam().App().Lam().AssertAll(() => CheckLen(canonical.Length)).App().Builtin(51).Data(raw);
                break;
            case "ser":
                int l = canonical.Length;
                int k = Math.Min(64, l);
                void Slice(int start) => _ = p.App().App().Builtin(15).App().App().App().Builtin(12).Int(start).Int(k).Var(1).Bytes(canonical[start..(start + k)]);
                _ = p.Lam().App().Lam().AssertAll(() => CheckLen(l), () => Slice(0), () => Slice(l / 2), () => Slice(l - k)).App().Builtin(51).Data(raw);
                break;
            default:
                Assert.Fail($"Unknown body {body}");
                break;
        }

        return p.Finish();
    }

    private sealed class FlatProbe
    {
        private readonly BitWriter _w = new();

        internal FlatProbe(int major, int minor, int patch)
        {
            FlatEncoder.EncodeNaturalCore(_w, (ulong)major);
            FlatEncoder.EncodeNaturalCore(_w, (ulong)minor);
            FlatEncoder.EncodeNaturalCore(_w, (ulong)patch);
        }

        internal byte[] Finish()
        {
            _w.Pad();
            return _w.ToArray();
        }

        internal FlatProbe Bit(int bit)
        {
            _w.PushBit(bit);
            return this;
        }

        internal FlatProbe Delay() => Tag(1);

        internal FlatProbe Lam() => Tag(2);

        internal FlatProbe App() => Tag(3);

        internal FlatProbe Force() => Tag(5);

        internal FlatProbe Err() => Tag(6);

        internal FlatProbe Case() => Tag(9);

        internal FlatProbe Var(int i)
        {
            _ = Tag(0);
            FlatEncoder.EncodeNaturalCore(_w, (ulong)i);
            return this;
        }

        internal FlatProbe Constr(ulong tag)
        {
            _ = Tag(8);
            FlatEncoder.EncodeNaturalCore(_w, tag);
            return this;
        }

        internal FlatProbe Builtin(int tag)
        {
            _ = Tag(7);
            _w.PushBits(tag, 7);
            return this;
        }

        internal FlatProbe Unit() => Const(3);

        internal FlatProbe Int(long n)
        {
            _ = Const(0);
            FlatEncoder.EncodeInteger(_w, n);
            return this;
        }

        internal FlatProbe Bytes(byte[] b)
        {
            _ = Const(1);
            FlatEncoder.EncodeByteString(_w, b);
            return this;
        }

        internal FlatProbe Data(byte[] cbor)
        {
            _ = Const(8);
            FlatEncoder.EncodeByteString(_w, cbor);
            return this;
        }

        internal FlatProbe AssertAll(params Action[] conds)
        {
            foreach (Action cond in conds)
            {
                _ = Force().App().App().App().Force().Builtin(26);
                cond();
                _ = Delay();
            }

            _ = Unit();
            for (int i = 0; i < conds.Length; i++)
            {
                _ = Delay().Err();
            }

            return this;
        }

        private FlatProbe Tag(int tag)
        {
            _w.PushBits(tag, 4);
            return this;
        }

        private FlatProbe Const(int typeTag)
        {
            _w.PushBits(4, 4);
            _w.PushBit(1);
            _w.PushBits(typeTag, 4);
            _w.PushBit(0);
            return this;
        }
    }
}

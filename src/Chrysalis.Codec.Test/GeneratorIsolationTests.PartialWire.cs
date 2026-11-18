using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    public static TheoryData<bool, bool, int, bool, int, bool, int> PartialWireCases()
    {
        TheoryData<bool, bool, int, bool, int, bool, int> rows = [];
        bool[] variants = [false, true];
        (int Count, bool Split)[] layouts = [(2, false), (2, true), (3, true)];
        foreach (bool isStruct in variants)
        {
            foreach (bool positional in variants)
            {
                foreach ((int count, bool split) in layouts)
                {
                    for (int marker = 0; marker < count; marker++)
                    {
                        rows.Add(isStruct, positional, count, split, marker, false, 0);
                        rows.Add(isStruct, positional, count, split, marker, true, 0);
                    }
                }
            }
        }
        // Reverse the field types, and test sparse order numbers with the marker on an empty part.
        foreach (bool isStruct in variants)
        {
            foreach (bool reverse in variants)
            {
                rows.Add(isStruct, true, 2, true, 1, reverse, 1);
                rows.Add(isStruct, true, 3, true, 2, reverse, 2);
            }
        }
        // A single declaration keeps wire order even when its annotations disagree.
        rows.Add(true, false, 1, false, 0, false, 3);
        return rows;
    }

    [Theory]
    [MemberData(nameof(PartialWireCases))]
    public void PartialRecordWriterAndUnionReaderAgree(bool isStruct, bool positional, int count, bool split, int marker, bool reverse, int orderVariant)
    {
        string kind = isStruct ? "readonly partial record struct" : "partial record";
        string field = isStruct ? "partial " : "";
        bool intFirst = orderVariant == 1;
        int tagOrder = orderVariant == 0 ? 0 : 1;
        int versionOrder = orderVariant switch { 1 or 3 => 0, 2 => 99, _ => 1 };
        string tag = $"[CborOrder({tagOrder})] public {field}string Tag {{ get; }}";
        string version = $"[CborOrder({versionOrder})] public {field}int Version {{ get; }}";
        string tagParameter = $"[CborOrder({tagOrder})] string Tag";
        string versionParameter = $"[CborOrder({versionOrder})] int Version";
        string parameters = !positional ? "" : !split ? $"({tagParameter}, {versionParameter})" : $"({(intFirst ? tagParameter : versionParameter)})";
        string constructor = isStruct ? "" : !positional ? "public Split(string tag, int version) => (Tag, Version) = (tag, version);"
            : intFirst ? "public Split(int version, string tag) : this(tag) => Version = version;"
            : "public Split(string tag, int version) : this(version) => Tag = tag;";
        string[] declarations = new string[count];
        string positionalBody = intFirst ? $"{tag} public string Initial {{ get; }} = Tag;"
            : $"{(split ? "" : tag)} {version} public int Initial {{ get; }} = Version;";
        declarations[0] = positional ? (isStruct ? positionalBody : "") : $"{(split ? "" : tag)} {version}";
        if (split)
        {
            declarations[1] = $"{(intFirst ? version : tag)} {constructor}";
        }
        else if (!positional)
        {
            declarations[0] += " " + constructor;
        }
        string inheritance = isStruct ? "IChoice" : "CborRecord, IChoice";
        // Create takes positional record parameters before body members, regardless of wire order.
        string createArguments = positional && split && !intFirst ? "7, \"x\"" : "\"x\", 7";
        SyntaxTree root = ParseIncremental("Root.cs", $$"""
            using System.Buffers;
            namespace Snapshot;
            [CborSerializable, CborUnion] public partial interface IChoice : ICborType;
            [CborSerializable, CborList] public partial record Flags([CborOrder(0)] bool Left, [CborOrder(1)] bool Right) : CborRecord, IChoice;
            public static class Probe
            {
                public static byte[] CreateBytes() => Split.Create({{createArguments}}).Raw.ToArray();
                public static byte[] WriteBytes(byte[] bytes)
                {
                    Split model = Split.Read(bytes);
                    Split fresh = model with { {{(isStruct ? "_raw" : "Raw")}} = default };
                    ArrayBufferWriter<byte> output = new();
                    Split.Write(output, fresh);
                    return output.WrittenSpan.ToArray();
                }
                public static bool ReadEquals(byte[] bytes) => IChoice.Read(bytes) is Split { Tag: "x", Version: 7 };
            }
            """);
        SyntaxTree[] parts = [.. Enumerable.Range(0, count).Select(index => ParseIncremental($"Part{(reverse ? count - 1 - index : index)}.cs", $$"""
            namespace Snapshot;
            {{(marker == index ? "[CborSerializable, CborList]" : "")}}
            public {{kind}} Split{{(index == 0 ? parameters + " : " + inheritance : "")}} { {{declarations[index]}} }
            """, includeGlobals: false))];
        SyntaxTree[] trees = [root, .. reverse ? parts.Reverse() : parts];
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(IncrementalCompilation("PartialWireProbe", trees),
            out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = output.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Type probe = Assembly.Load(image.ToArray()).GetType("Snapshot.Probe", throwOnError: true)!;
        Func<byte[]> create = probe.GetMethod("CreateBytes")!.CreateDelegate<Func<byte[]>>();
        Func<byte[], byte[]> write = probe.GetMethod("WriteBytes")!.CreateDelegate<Func<byte[], byte[]>>();
        Func<byte[], bool> equals = probe.GetMethod("ReadEquals")!.CreateDelegate<Func<byte[], bool>>();

        byte[] expected = intFirst ? [0x82, 0x07, 0x61, 0x78] : [0x82, 0x61, 0x78, 0x07];
        byte[] created = create();
        Assert.Equal(expected, created);
        Assert.True(equals(created));
        byte[] written = write(expected);
        Assert.Equal(expected, written);
        Assert.True(equals(written));
    }
}

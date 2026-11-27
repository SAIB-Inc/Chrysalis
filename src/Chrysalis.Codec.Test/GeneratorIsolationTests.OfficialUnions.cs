using System.Reflection;
using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    [Fact]
    public void SemanticWireAttributesAgreeBetweenGeneratedCasesAndOfficialUnions()
    {
        const string imports = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            """;
        byte[] library = EmitUnionProbe(CreateCompilation("SemanticWireCases", imports + "\n" + """
            namespace SemanticWireCases;
            public static class Tags { public const int Tag42 = 42; }
            [CborSerializable, CborTag(tag: 42)]
            public readonly partial record struct ExternalNamed : ICborType { public partial string Text { get; } }
            [CborSerializable, CborTag(Tags.Tag42)]
            public readonly partial record struct ExternalConstant : ICborType { public partial string Text { get; } }
            """));
        const string host = """
            using SemanticWireCases;
            namespace SemanticWireHost;

            public static class Tags { public const int Tag42 = 42; }

            [CborSerializable, CborTag(Tags.Tag42)]
            public readonly partial record struct Constant<T> : ICborType { public partial T Value { get; } }
            [CborSerializable, CborTag(tag: 42)]
            public readonly partial record struct Named<T> : ICborType { public partial T Value { get; } }
            [CborSerializable, CborTagAttribute(42)]
            public readonly partial record struct Suffixed<T> : ICborType { public partial T Value { get; } }
            [CborSerializable, global::Chrysalis.Codec.Serialization.Attributes.CborTag(42)]
            public readonly partial record struct Qualified<T> : ICborType { public partial T Value { get; } }

            [CborSerializable, CborUnion(typeof(Constant<string>)), Union] public readonly partial struct U1 : ICborType;
            [CborSerializable, CborUnion(typeof(Named<string>)), Union] public readonly partial struct U2 : ICborType;
            [CborSerializable, CborUnion(typeof(Suffixed<string>)), Union] public readonly partial struct U3 : ICborType;
            [CborSerializable, CborUnion(typeof(Qualified<string>)), Union] public readonly partial struct U4 : ICborType;
            [CborSerializable, CborUnion(typeof(ExternalNamed)), Union] public readonly partial struct U5 : ICborType;
            [CborSerializable, CborUnion(typeof(ExternalConstant)), Union] public readonly partial struct U6 : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] tag = [0xD8, 0x2A, 0x61, 0x78];
                    var c1 = Constant<string>.Create("x"); Require(c1.Raw.Span, tag, nameof(Constant<string>));
                    var c2 = Named<string>.Create("x"); Require(c2.Raw.Span, tag, nameof(Named<string>));
                    var c3 = Suffixed<string>.Create("x"); Require(c3.Raw.Span, tag, nameof(Suffixed<string>));
                    var c4 = Qualified<string>.Create("x"); Require(c4.Raw.Span, tag, nameof(Qualified<string>));
                    var c5 = ExternalNamed.Create("x"); Require(c5.Raw.Span, tag, nameof(ExternalNamed));
                    var c6 = ExternalConstant.Create("x"); Require(c6.Raw.Span, tag, nameof(ExternalConstant));
                    U2 u2 = c2;
                    RoundTrip(new U1(c1), U1.Read, tag); RoundTrip(u2, U2.Read, tag); RoundTrip(new U3(c3), U3.Read, tag);
                    RoundTrip(new U4(c4), U4.Read, tag); RoundTrip(new U5(c5), U5.Read, tag); RoundTrip(new U6(c6), U6.Read, tag);
                }
            """;
        const string assertions = """
                private static void RoundTrip<T>(T value, Func<ReadOnlyMemory<byte>, T> read, byte[] expected) where T : ICborType
                {
                    byte[] wire = CborSerializer.Serialize(value);
                    Require(wire, expected, typeof(T).Name);
                    Require(CborSerializer.Serialize(read(wire)), expected, typeof(T).Name);
                }

                private static void Require(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, string type)
                {
                    if (!actual.SequenceEqual(expected))
                        throw new InvalidOperationException($"{type}: expected {Convert.ToHexString(expected)}, actual {Convert.ToHexString(actual)}.");
                }
            }
            """;
        byte[] image = EmitUnionProbe(CreateCompilation("SemanticWireHost", $"{imports}\n{host}\n{assertions}")
            .AddReferences(MetadataReference.CreateFromImage(library)));
        System.Runtime.Loader.AssemblyLoadContext context = new("SemanticWireProbe", isCollectible: true);
        try
        {
            using MemoryStream libraryStream = new(library);
            _ = context.LoadFromStream(libraryStream);
            using MemoryStream imageStream = new(image);
            RunUnionProbe(context.LoadFromStream(imageStream), "SemanticWireHost");
        }
        finally
        {
            context.Unload();
        }

        const string properties = """
            namespace SemanticProperties;
            public static class Orders { public const int First = 0; public const int ConstructorIndex = 1; }
            [CborSerializable, CborList, CborIndex(index: 1)]
            public partial record Positional(
                [property: CborOrder(Orders.First)] int Index,
                [CborOrder(order: 1)] int Count,
                [CborOrderAttribute(2), CborSize(size: 64)] byte[] Payload) : CborRecord;
            [CborSerializable, CborUnion(typeof(Positional)), Union] public readonly partial struct U7 : ICborType;

            [CborSerializable, CborConstr(Orders.ConstructorIndex)]
            public readonly partial record struct Constr : ICborType { [CborOrder(order: 0)] public partial int A { get; } }
            [PlutusData(constructorIndex: 0)]
            public readonly partial record struct Datum : ICborType { [CborOrderAttribute(0)] public partial int A { get; } }
            [CborSerializable, CborUnion(typeof(Constr), typeof(Datum)), Union] public readonly partial struct U8 : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] chunk = new byte[64];
                    byte[] payload = [..chunk, 0x2A];
                    byte[] wire = [0x83, 0x01, 0x02, 0x5F, 0x58, 0x40, ..chunk, 0x41, 0x2A, 0xFF];
                    RoundTrip(new U7(Positional.Create(1, 2, payload)), U7.Read, wire);
                    if (U7.Read(wire) is not Positional { Index: 1, Count: 2, Payload.Length: 65 })
                        throw new InvalidOperationException("Positional properties did not round-trip in wire order.");
                    RoundTrip(new U8(Constr.Create(2)), U8.Read, [0xD8, 0x7A, 0x81, 0x02]);
                    RoundTrip(new U8(Datum.Create(3)), U8.Read, [0xD8, 0x79, 0x81, 0x03]);
                }
            """;
        RunUnionProbe(Assembly.Load(EmitUnionProbe(CreateCompilation("SemanticProperties", $"{imports}\n{properties}\n{assertions}"))), "SemanticProperties");
    }

    [Fact]
    public void OptionalBeforeRequiredUnionTupleDoesNotMatchShortSibling()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace OptionalTupleProbe;

            [CborSerializable, CborList]
            public readonly partial record struct OptBeforeReq : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial int? B { get; }
                [CborOrder(2)] public partial int? C { get; }
                [CborOrder(3)] public partial int D { get; }
            }
            [CborSerializable, CborList]
            public readonly partial record struct OptTail : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial int? B { get; }
                [CborOrder(2)] public partial string? C { get; }
                [CborOrder(3)] public partial string? D { get; }
            }
            [CborSerializable, CborUnion(typeof(OptBeforeReq), typeof(OptTail)), Union]
            public readonly partial struct PairUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[][] tails = [[0x82, 1, 2], [0x9F, 1, 0xFF], [0x84, 1, 0xF6, 0x61, 0x61, 0x61, 0x62]];
                    foreach (byte[] wire in tails)
                    {
                        PairUnion read = PairUnion.Read(wire);
                        if (read is not OptTail { A: 1 } || !CborSerializer.Serialize(read).AsSpan().SequenceEqual(wire) ||
                            !CborSerializer.Serialize(new PairUnion(OptTail.Read(wire))).AsSpan().SequenceEqual(wire))
                            throw new InvalidOperationException("OptTail failed to round-trip.");
                    }
                    byte[][] full = [[0x84, 1, 0xF6, 0xF6, 4], [0x9F, 1, 0xF6, 0xF6, 4, 0xFF]];
                    foreach (byte[] wire in full)
                    {
                        PairUnion read = PairUnion.Read(wire);
                        if (read is not OptBeforeReq { A: 1, D: 4 } || !CborSerializer.Serialize(read).AsSpan().SequenceEqual(wire) ||
                            !CborSerializer.Serialize(new PairUnion(OptBeforeReq.Read(wire))).AsSpan().SequenceEqual(wire))
                            throw new InvalidOperationException("OptBeforeReq failed to round-trip.");
                    }
                }
            }
            """;
        RunUnionProbe(Assembly.Load(EmitUnionProbe(CreateCompilation("OptionalTupleProbe", source))), "OptionalTupleProbe");
    }

    [Fact]
    public void UnionArrayLengthLookaheadCapsDefiniteAndIndefiniteArrays()
    {
        (string Wire, int Count)[] cases =
        [
            ("80", 0), ("8101", 1), ("820102", 2), ("850102030405", 2),
            ("9FFF", 0), ("9F01FF", 1), ("9F0102FF", 2), ("9F0102", 2), ("9F", -1), ("9F01", -1),
        ];
        foreach ((string wire, int expected) in cases)
        {
            Assert.Equal(expected, CborUnionWire.ReadArrayLength(Convert.FromHexString(wire), 1));
        }
        Assert.Equal(1, CborUnionWire.ReadArrayLength([0x82, 1, 2], -1));
        Assert.Equal(1, CborUnionWire.ReadArrayLength([0x9F, 1, 2], -1));
    }

    [Fact]
    public void CollidingTaggedListsEmitCHCB001InsteadOfAnUntaggedMatcher()
    {
        const string source = """
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace TaggedListCollision;
            [CborSerializable, CborList, CborTag(42)]
            public readonly partial record struct WithInt : ICborType { [CborOrder(0)] public partial int Value { get; } }
            [CborSerializable, CborList, CborTag(42)]
            public readonly partial record struct WithString : ICborType { [CborOrder(0)] public partial string Value { get; } }
            [CborSerializable, CborUnion(typeof(WithInt), typeof(WithString)), Union]
            public readonly partial struct Choice : ICborType;
            """;
        GeneratorDriverRunResult result = CreateDriver().RunGenerators(
            CreateCompilation("TaggedListCollision", source)).GetRunResult();
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "CHCB001");
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.DoesNotContain(result.Results.Single().GeneratedSources, s => s.SourceText.ToString().Contains("__Matches", StringComparison.Ordinal));
    }

    private static byte[] EmitUnionProbe(CSharpCompilation input)
    {
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return image.ToArray();
    }

    private static void RunUnionProbe(Assembly assembly, string probeNamespace) =>
        assembly.GetType($"{probeNamespace}.Probe", throwOnError: true)!.GetMethod("Exercise")!.CreateDelegate<Action>()();

    [Fact]
    public Task OfficialStructUnionSnapshot() => VerifySource("""
        using System.Runtime.CompilerServices;
        [CborSerializable, CborUnion(typeof(First), typeof(Second)), Union]
        public readonly partial struct Choice : ICborType;
        [CborSerializable, CborList, CborIndex(0)]
        public readonly partial record struct First : ICborType
        {
            [CborOrder(0)] public partial int Tag { get; }
            [CborOrder(1)] public partial int Value { get; }
        }
        [CborSerializable]
        public readonly partial record struct Second : ICborType
        {
            public partial string Text { get; }
        }
        """);

    [Theory]
    [InlineData("[CborSerializable, CborUnion(typeof(First))] public readonly partial struct Bad : ICborType;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First)), Union] public partial struct Bad : ICborType;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First)), Union] public readonly partial record struct Bad : ICborType;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First)), Union] internal readonly partial struct Bad : ICborType;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First)), Union] public readonly ref partial struct Bad : ICborType;", "CHCB005")]
    [InlineData("public static class Outer { [CborSerializable, CborUnion(typeof(First)), Union] public readonly partial struct Bad : ICborType; }", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First)), Union, CborMap] public readonly partial struct Bad : ICborType;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First)), Union, CborList] public readonly partial struct Bad : ICborType;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First)), Union, CborConstr(0)] public readonly partial struct Bad : ICborType;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First)), Union, PlutusData(0)] public readonly partial struct Bad : ICborType;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First)), Union] public readonly partial struct Bad;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion(typeof(First))] public partial interface Bad : ICborType;", "CHCB005")]
    [InlineData("[CborSerializable, CborUnion, Union] public readonly partial struct Bad : ICborType;", "CHCB006")]
    [InlineData("[CborSerializable, CborUnion(typeof(Box<int>), typeof(Box<int>)), Union] public readonly partial struct Bad : ICborType;", "CHCB006")]
    [InlineData("[CborSerializable, CborUnion(typeof(Bad)), Union] public readonly partial struct Bad : ICborType;", "CHCB006")]
    [InlineData("[CborSerializable, CborUnion(typeof(Box<,>)), Union] public readonly partial struct Bad<T> : ICborType;", "CHCB006")]
    [InlineData("[CborSerializable, CborUnion(typeof(Box<>)), Union] public readonly partial struct Bad : ICborType;", "CHCB006")]
    [InlineData("[CborSerializable, CborUnion(typeof(ICaseIface)), Union] public readonly partial struct Bad : ICborType;", "CHCB007")]
    [InlineData("[CborSerializable, CborUnion(typeof(AbsCase)), Union] public readonly partial struct Bad : ICborType;", "CHCB007")]
    [InlineData("[CborSerializable, CborUnion(typeof(InnerUnion)), Union] public readonly partial struct Bad : ICborType;", "CHCB007")]
    [InlineData("[CborSerializable, CborUnion(typeof(NoIface)), Union] public readonly partial struct Bad : ICborType;", "CHCB007")]
    [InlineData("[CborSerializable, CborUnion(typeof(PlainStruct)), Union] public readonly partial struct Bad : ICborType;", "CHCB007")]
    [InlineData("[CborSerializable, CborUnion(typeof(RefStructCase)), Union] public readonly partial struct Bad : ICborType;", "CHCB007")]
    public void OfficialUnionDeclarationDiagnosticsEmitExpectedError(string declaration, string expectedId)
    {
        string source = $$"""
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace DiagProbe;
            [CborSerializable] public readonly partial record struct First : ICborType { public partial int Value { get; } }
            [CborSerializable] public readonly partial record struct Box<T> : ICborType { public partial T Value { get; } }
            [CborSerializable, CborUnion] public partial interface ICaseIface : ICborType;
            [CborSerializable, CborUnion] public abstract partial record AbsCase : Chrysalis.Codec.Types.CborRecord;
            [CborSerializable, CborUnion(typeof(First)), Union] public readonly partial struct InnerUnion : ICborType;
            [CborSerializable] public readonly partial record struct NoIface { public partial int Value { get; } }
            public readonly struct PlainStruct : ICborType
            {
                public ReadOnlyMemory<byte> Raw => default;
                public int ConstrIndex => 0;
                public bool IsIndefinite => false;
            }
            public readonly ref struct RefStructCase : ICborType
            {
                public ReadOnlyMemory<byte> Raw => default;
                public int ConstrIndex => 0;
                public bool IsIndefinite => false;
                public static RefStructCase Read(ReadOnlyMemory<byte> data, out int bytesConsumed) { _ = data; bytesConsumed = 0; return default; }
                public static void Write(System.Buffers.IBufferWriter<byte> output, RefStructCase value) { _ = output; _ = value; }
            }
            {{declaration}}
            """;
        GeneratorDriverRunResult result = CreateDriver().RunGenerators(CreateCompilation("DiagProbe", source)).GetRunResult();
        Diagnostic diag = Assert.Single(result.Diagnostics, d => d.Id == expectedId);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.True(diag.Location.IsInSource);
        Assert.DoesNotContain(result.Results.Single().GeneratedSources, s => s.HintName.Contains("Bad.", StringComparison.Ordinal));
    }

    [Fact]
    public void OfficialUnionNearMissReadEmitsCHCB003WithoutCS0111()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace NearMissProbe;
            [CborSerializable] public readonly partial record struct First : ICborType { public partial int Value { get; } }
            [CborSerializable, CborUnion(typeof(First)), Union]
            public readonly partial struct Choice : ICborType
            {
                public static Choice Read(ReadOnlyMemory<byte> data) => throw new NotImplementedException();
            }
            """;
        GeneratorDriverRunResult result = CreateDriver().RunGeneratorsAndUpdateCompilation(
            CreateCompilation("NearMissProbe", source), out Compilation output, out _).GetRunResult();
        Diagnostic diag = Assert.Single(result.Diagnostics, d => d.Id == "CHCB003");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.True(diag.Location.IsInSource);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Id == "CS0111");
    }

    [Fact]
    public void OpenAndClosedGenericStructUnionsAndUnionHintCompileAndRoundTrip()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace GenericUnionProbe;

            [CborSerializable, CborUnion(typeof(OptSome<>), typeof(OptNone<>), typeof(OptEmpty<>)), Union]
            public readonly partial struct Opt<TItem> : ICborType;

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct OptSome<T> : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial T Value { get; }
            }

            [CborSerializable, CborList, CborIndex(1)]
            public readonly partial record struct OptNone<T> : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
            }

            [CborSerializable, CborConstr(1)]
            public readonly partial record struct OptEmpty<T> : ICborType;

            [CborSerializable, CborUnion(typeof(OptSome<int>), typeof(OptNone<int>)), Union]
            public readonly partial struct IntOption : ICborType;

            [CborSerializable]
            public readonly partial record struct Box<T> : ICborType { public partial T Value { get; } }

            [CborSerializable, CborUnion(typeof(Box<int>), typeof(Box<string>)), Union]
            public readonly partial struct TwoBoxes : ICborType;

            [CborSerializable, CborUnion(typeof(Box<int>), typeof(Box<uint>)), Union]
            public readonly partial struct SignedOrUnsigned : ICborType;

            [CborSerializable, CborList]
            public readonly partial record struct HintedContainer : ICborType
            {
                [CborOrder(0)] public partial int Kind { get; }
                [CborOrder(1), CborUnionHint(nameof(Kind), 0, typeof(OptSome<int>))] public partial IntOption Item { get; }
            }

            public static class Probe
            {
                public static string Exercise()
                {
                    Opt<int> some = OptSome<int>.Create(0, 99);
                    Opt<int> none = OptNone<int>.Create(1);
                    Opt<int> empty = new OptEmpty<int>();
                    if (!empty.Raw.Span.SequenceEqual(Convert.FromHexString("D87A80")))
                        throw new InvalidOperationException("0-field OptEmpty<int> EncodeFresh mismatch.");
                    try { _ = new Opt<int>(new OptNone<int>()); throw new InvalidOperationException("Uninitialized OptNone<int> must fail."); }
                    catch (CborValidationException) { }
                    byte[] someWire = CborSerializer.Serialize(some);
                    Opt<int> backSome = Opt<int>.Read(someWire);
                    Opt<int> backNone = Opt<int>.Read(CborSerializer.Serialize(none));
                    Opt<int> backEmpty = Opt<int>.Read(CborSerializer.Serialize(empty));
                    IntOption closed = OptSome<int>.Create(0, 7);
                    byte[] hintedBytes = CborSerializer.Serialize(HintedContainer.Create(0, closed));
                    HintedContainer hinted = HintedContainer.Read(hintedBytes);
                    TwoBoxes pos = TwoBoxes.Read(Convert.FromHexString("182A"));
                    TwoBoxes neg = TwoBoxes.Read(Convert.FromHexString("26"));
                    TwoBoxes txt = TwoBoxes.Read(Convert.FromHexString("626869"));
                    SignedOrUnsigned s = SignedOrUnsigned.Read(Convert.FromHexString("20"));
                    if (backSome is not OptSome<int> { Value: 99 } || backNone is not OptNone<int> || backEmpty is not OptEmpty<int>)
                        throw new InvalidOperationException("Open generic round-trip failed.");
                    if (hinted.Item is not OptSome<int> { Value: 7 })
                        throw new InvalidOperationException("UnionHint dispatch failed.");
                    if (pos is not Box<int> { Value: 42 } || neg is not Box<int> { Value: -7 } || txt is not Box<string> { Value: "hi" })
                        throw new InvalidOperationException("TwoBoxes dispatch failed.");
                    if (s is not Box<int> { Value: -1 })
                        throw new InvalidOperationException("SignedOrUnsigned dispatch failed.");
                    return $"{pos}|{txt}";
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("GenericUnionProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        string actual = (string)Assembly.Load(image.ToArray()).GetType("GenericUnionProbe.Probe", true)!.GetMethod("Exercise")!.Invoke(null, null)!;
        Assert.Equal("TwoBoxes(Box<int>)|TwoBoxes(Box<string>)", actual);
    }

    [Fact]
    public void CrossAssemblyExternalCasesMatchSourceParityAndFailClosedOnAmbiguousOrder() =>
        VerifyCrossAssemblyExternalCases(useCompilationReference: false);

    [Fact]
    public void CrossAssemblyCompilationReferenceMatchesSourceParityWithoutCS8785() =>
        VerifyCrossAssemblyExternalCases(useCompilationReference: true);

    private static void VerifyCrossAssemblyExternalCases(bool useCompilationReference)
    {
        const string externalSource = """
            #nullable enable
            global using System;
            global using System.Buffers;
            global using System.Linq;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace ExternalCases;

            [CborSerializable] public readonly partial record struct ExtPot : ICborType { public partial ulong Amount { get; } }
            [CborSerializable] public readonly partial record struct ExtCreds : ICborType { public partial System.Collections.Generic.Dictionary<int, long> Value { get; } }
            [CborSerializable, CborList]
            public readonly partial record struct ExtDisplay : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                public string Display => "x";
                public static ExtDisplay Create(string text, int radix) => Create(Convert.ToInt32(text, radix));
                public static ExtDisplay Create(int tag, string display) => Create(tag);
            }
            [CborSerializable, CborList]
            public partial record ExtPositional([CborOrder(0)] int Tag, [CborOrder(1)] int Count) : CborRecord;
            [CborSerializable, CborList]
            public readonly partial record struct ExtNullableRef : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial string? Name { get; }
            }
            public readonly struct ExtManual : ICborType
            {
                public ReadOnlyMemory<byte> Raw { get; }
                public int ConstrIndex => 0;
                public bool IsIndefinite => false;
                private ExtManual(ReadOnlyMemory<byte> raw) => Raw = raw;
                public static ExtManual Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
                {
                    SAIB.Cbor.Serialization.CborReader r = new(data.Span);
                    _ = r.ReadByteString();
                    bytesConsumed = data.Length - r.Buffer.Length;
                    return new(data[..bytesConsumed]);
                }
                public static void Write(IBufferWriter<byte> output, ExtManual value) =>
                    new SAIB.Cbor.Serialization.CborWriter(output).WriteByteString(value.Raw.Span);
            }
            [CborSerializable, CborList, CborIndex(1)]
            public readonly partial record struct ExtHostName : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial int? Port { get; }
                [CborOrder(2)] public partial string DnsName { get; }
            }
            [CborSerializable, CborList, CborIndex(2)]
            public readonly partial record struct ExtOtherList : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial byte[] Data { get; }
            }
            [CborSerializable, CborList]
            public readonly partial record struct ExtVrfCert : ICborType
            {
                [CborOrder(0)] public partial ReadOnlyMemory<byte> Output { get; }
                [CborOrder(1)] public partial ReadOnlyMemory<byte> Proof { get; }
            }
            [CborSerializable, CborList]
            public partial record ExtReversedSinglePart : CborRecord
            {
                [CborOrder(1)] public string Tag { get; }
                [CborOrder(0)] public int Version { get; }
                public ExtReversedSinglePart(string tag, int version) => (Tag, Version) = (tag, version);
            }
            [CborSerializable]
            public readonly partial record struct ExtAmbigCreate : ICborType
            {
                public partial int First { get; }
                public int Computed => 0;
                public static ExtAmbigCreate Create(int first, int computed) => Create(first);
            }
            """;

        CSharpCompilation extComp = CreateCompilation("ExternalCases", externalSource);
        _ = CreateDriver().RunGeneratorsAndUpdateCompilation(extComp, out Compilation extOut, out _);
        using MemoryStream extMs = new();
        Microsoft.CodeAnalysis.Emit.EmitResult extEmit = extOut.Emit(extMs);
        Assert.True(extEmit.Success, string.Join(Environment.NewLine, extEmit.Diagnostics));
        byte[] extBytes = extMs.ToArray();
        MetadataReference extRef = useCompilationReference
            ? extOut.ToMetadataReference()
            : MetadataReference.CreateFromImage(extBytes);


        const string hostSource = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using ExternalCases;
            namespace HostParity;

            [CborSerializable, CborUnion(typeof(ExtPot), typeof(ExtCreds)), Union]
            public readonly partial struct TargetExt : ICborType;

            [CborSerializable, CborUnion(typeof(ExtDisplay), typeof(ExtPositional), typeof(ExtNullableRef)), Union]
            public readonly partial struct MiscExt : ICborType;

            [CborSerializable, CborUnion(typeof(ExtDisplay)), Union]
            public readonly partial struct SingleDisplayExt : ICborType;

            [CborSerializable, CborUnion(typeof(ExtHostName), typeof(ExtOtherList)), Union]
            public readonly partial struct ComplexExt : ICborType;

            [CborSerializable, CborUnion(typeof(ExtPot), typeof(ExtVrfCert)), Union]
            public readonly partial struct VrfUnion : ICborType;

            [CborSerializable, CborUnion(typeof(ExtManual), typeof(ExtHostName)), Union]
            public readonly partial struct ManualDeclaredExt : ICborType
            {
                public static ManualDeclaredExt Read(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
                    !data.IsEmpty && (data.Span[0] >> 5) == 2
                        ? new(ExtManual.Read(data, out bytesConsumed))
                        : new(ExtHostName.Read(data, out bytesConsumed));
            }
            """;

        CSharpCompilation hostComp = CSharpCompilation.Create("HostParity", [CSharpSyntaxTree.ParseText(hostSource, ParseOptions)],
            [.. References, extRef], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver hostDriver = CreateDriver().RunGeneratorsAndUpdateCompilation(hostComp, out Compilation hostOut, out _);
        GeneratorDriverRunResult hostResult = hostDriver.GetRunResult();
        Assert.Null(hostResult.Results.Single().Exception);
        Assert.DoesNotContain(hostResult.Diagnostics, d => d.Id == "CS8785" || d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(hostOut.GetDiagnostics(), d => d.Id == "CS8785" || d.Severity == DiagnosticSeverity.Error);
        using MemoryStream hostMs = new();
        Microsoft.CodeAnalysis.Emit.EmitResult hostEmit = hostOut.Emit(hostMs);
        Assert.True(hostEmit.Success, string.Join(Environment.NewLine, hostEmit.Diagnostics));

        // Compile the exact same case + union declarations in-source and verify generated serializer parity across every union.
        string localExtSource = externalSource.Replace("namespace ExternalCases;", "namespace CombinedCases;", StringComparison.Ordinal);
        string localHostSource = hostSource
            .Replace("global using System;\n", "", StringComparison.Ordinal)
            .Replace("using ExternalCases;", "using CombinedCases;", StringComparison.Ordinal)
            .Replace("namespace HostParity;", "namespace CombinedParity;", StringComparison.Ordinal);
        CSharpCompilation localComp = CreateCompilation("CombinedParity", localExtSource)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(localHostSource, ParseOptions));
        GeneratorDriver localDriver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            localComp, out Compilation localOut, out _);
        Assert.Empty(localOut.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        foreach (string unionName in (string[])["TargetExt", "MiscExt", "ComplexExt", "VrfUnion", "ManualDeclaredExt", "SingleDisplayExt"])
        {
            string localSer = GeneratedText(localDriver, "CombinedParity." + unionName)
                .Replace("CombinedParity", "HostParity", StringComparison.Ordinal)
                .Replace("CombinedCases", "ExternalCases", StringComparison.Ordinal);
            string hostSer = GeneratedText(hostDriver, "HostParity." + unionName);
            Assert.Equal(localSer, hostSer);
        }

        string complexSer = GeneratedText(hostDriver, "HostParity.ComplexExt");
        Assert.Contains("ValidateReadListSlice", complexSer, StringComparison.Ordinal);

        System.Runtime.Loader.AssemblyLoadContext alc = new("ParityAlc", isCollectible: true);
        try
        {
            using MemoryStream extLoad = new(extBytes);
            Assembly extAsm = alc.LoadFromStream(extLoad);
            alc.Resolving += (_, name) => string.Equals(name.Name, "ExternalCases", StringComparison.Ordinal) ? extAsm : null;
            using MemoryStream hostLoad = new(hostMs.ToArray());
            Assembly hostAsm = alc.LoadFromStream(hostLoad);
            Type complexType = hostAsm.GetType("HostParity.ComplexExt", true)!;
            Type vrfUnionType = hostAsm.GetType("HostParity.VrfUnion", true)!;
            Type manualUnionType = hostAsm.GetType("HostParity.ManualDeclaredExt", true)!;
            Type hostNameType = extAsm.GetType("ExternalCases.ExtHostName", true)!;
            Type vrfType = extAsm.GetType("ExternalCases.ExtVrfCert", true)!;
            MethodInfo complexRead = complexType.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
            MethodInfo vrfRead = vrfUnionType.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
            MethodInfo manualRead = manualUnionType.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;

            // Truncated optional-before-required ExtHostName [1, null] must fail both Read and ctor.
            byte[] truncatedHost = [0x82, 0x01, 0xF6];
            TargetInvocationException readErr = Assert.Throws<TargetInvocationException>(() => complexRead.Invoke(null, [new ReadOnlyMemory<byte>(truncatedHost)]));
            _ = Assert.IsType<CborDecodeException>(readErr.InnerException);

            object hostReadVal = hostNameType.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!.Invoke(null, [new ReadOnlyMemory<byte>(truncatedHost)])!;
            TargetInvocationException ctorErr = Assert.Throws<TargetInvocationException>(() =>
                complexType.GetConstructor([hostNameType])!.Invoke([hostReadVal]));
            _ = Assert.IsType<CborValidationException>(ctorErr.InnerException);

            // ExtVrfCert (Output property -> outputValue) constructs and round-trips cleanly.
            object vrf = vrfType.GetMethod("Create")!.Invoke(null, [new ReadOnlyMemory<byte>([1]), new ReadOnlyMemory<byte>([2])])!;
            object wrappedVrf = vrfUnionType.GetConstructor([vrfType])!.Invoke([vrf]);
            byte[] vrfRaw = ((ICborType)wrappedVrf).Raw.ToArray();
            Assert.True(vrfRead.Invoke(null, [new ReadOnlyMemory<byte>(vrfRaw)]) is IUnion { Value: not null });

            // ExtManual (manual Read/Write without CBOR shape attributes) constructs and round-trips via declared Read.
            Assert.True(manualRead.Invoke(null, [new ReadOnlyMemory<byte>([0x41, 0xAA])]) is IUnion { Value: not null });

            // ExtDisplay with unrelated convenience Create overloads keeps explicit [CborOrder(0)] Tag across both ref modes.
            Type singleDisplayType = hostAsm.GetType("HostParity.SingleDisplayExt", true)!;
            Type displayType = extAsm.GetType("ExternalCases.ExtDisplay", true)!;
            MethodInfo singleDisplayRead = singleDisplayType.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
            MethodInfo singleDisplayWrite = singleDisplayType.GetMethod("Write", [typeof(System.Buffers.IBufferWriter<byte>), singleDisplayType])!;
            object display7 = displayType.GetMethod("Create", [typeof(int)])!.Invoke(null, [7])!;
            Assert.Equal(new byte[] { 0x81, 0x07 }, ((ICborType)display7).Raw.ToArray());
            object wrappedDisplay = singleDisplayType.GetConstructor([displayType])!.Invoke([display7]);
            System.Buffers.ArrayBufferWriter<byte> displayBuf = new();
            _ = singleDisplayWrite.Invoke(null, [displayBuf, wrappedDisplay]);
            Assert.Equal(new byte[] { 0x81, 0x07 }, displayBuf.WrittenSpan.ToArray());
            object roundDisplay = singleDisplayRead.Invoke(null, [new ReadOnlyMemory<byte>([0x81, 0x07])])!;
            Assert.True(roundDisplay is IUnion { Value: ICborType displayCase } && displayCase.Raw.Span.SequenceEqual(new byte[] { 0x81, 0x07 }));
        }
        finally
        {
            alc.Unload();
        }

        // Single-part reversed [CborOrder] in referenced assembly must fail closed with CHCB007.
        const string badSource = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using ExternalCases;
            namespace BadParity;
            [CborSerializable, CborUnion(typeof(ExtReversedSinglePart)), Union]
            public readonly partial struct BadExt : ICborType;
            """;
        CSharpCompilation badComp = CSharpCompilation.Create("BadParity", [CSharpSyntaxTree.ParseText(badSource, ParseOptions)],
            [.. References, extRef], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriverRunResult badRes = CreateDriver().RunGenerators(badComp).GetRunResult();
        Assert.Null(badRes.Results.Single().Exception);
        Assert.DoesNotContain(badRes.Diagnostics, d => d.Id == "CS8785");
        _ = Assert.Single(badRes.Diagnostics, d => d.Id == "CHCB007");
        Assert.DoesNotContain(badRes.Results.Single().GeneratedSources, s => s.HintName.Contains("BadExt.", StringComparison.Ordinal));

        const string badAmbigSource = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using ExternalCases;
            namespace BadParity;
            [CborSerializable, CborUnion(typeof(ExtAmbigCreate)), Union]
            public readonly partial struct BadAmbigExt : ICborType;
            """;
        CSharpCompilation badAmbigComp = CSharpCompilation.Create("BadParity", [CSharpSyntaxTree.ParseText(badAmbigSource, ParseOptions)],
            [.. References, extRef], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriverRunResult badAmbigRes = CreateDriver().RunGenerators(badAmbigComp).GetRunResult();
        Assert.Null(badAmbigRes.Results.Single().Exception);
        Assert.DoesNotContain(badAmbigRes.Diagnostics, d => d.Id == "CS8785");
        _ = Assert.Single(badAmbigRes.Diagnostics, d => d.Id == "CHCB007");
        Assert.DoesNotContain(badAmbigRes.Results.Single().GeneratedSources, s => s.HintName.Contains("BadAmbigExt.", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedStructUnionHasZeroCS1591AndZeroGeneratedBoxingOrValueAccess()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace DocAndOpProbe;

            /// <summary>Choice union.</summary>
            [CborSerializable, CborUnion(typeof(First), typeof(Second)), Union]
            public readonly partial struct Choice : ICborType;
            /// <summary>First case.</summary>
            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct First : ICborType
            {
                /// <summary>Tag.</summary>
                [CborOrder(0)] public partial int Tag { get; }
                /// <summary>Value.</summary>
                [CborOrder(1)] public partial int Value { get; }
            }
            /// <summary>Second case.</summary>
            [CborSerializable]
            public readonly partial record struct Second : ICborType
            {
                /// <summary>Text.</summary>
                public partial string Text { get; }
            }
            /// <summary>Open generic union.</summary>
            /// <typeparam name="TItem">Item type.</typeparam>
            [CborSerializable, CborUnion(typeof(OptSome<>), typeof(OptNone<>)), Union]
            public readonly partial struct Opt<TItem> : ICborType;
            /// <summary>Present case.</summary>
            /// <typeparam name="TItem">Item type.</typeparam>
            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct OptSome<TItem> : ICborType
            {
                /// <summary>Tag.</summary>
                [CborOrder(0)] public partial int Tag { get; }
                /// <summary>Value.</summary>
                [CborOrder(1)] public partial TItem Value { get; }
            }
            /// <summary>Absent case.</summary>
            /// <typeparam name="TItem">Item type.</typeparam>
            [CborSerializable, CborList, CborIndex(1)]
            public readonly partial record struct OptNone<TItem> : ICborType
            {
                /// <summary>Tag.</summary>
                [CborOrder(0)] public partial int Tag { get; }
            }
            """;

        CSharpParseOptions docParse = new(LanguageVersion.Latest, DocumentationMode.Diagnose);
        CSharpCompilationOptions docOpts = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>
            {
                ["CS1591"] = ReportDiagnostic.Error,
                ["CS1584"] = ReportDiagnostic.Error,
                ["CS1658"] = ReportDiagnostic.Error,
                ["CS1574"] = ReportDiagnostic.Error
            });
        CSharpCompilation comp = CSharpCompilation.Create("DocAndOpProbe", [CSharpSyntaxTree.ParseText(source, docParse)], References, docOpts);
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create([new CodeGen.CborSerializerCodeGen().AsSourceGenerator()], parseOptions: docParse);
        GeneratorDriverRunResult runRes = driver.RunGeneratorsAndUpdateCompilation(comp, out Compilation outComp, out _).GetRunResult();
        Assert.Empty(runRes.Diagnostics);
        using MemoryStream pe = new();
        using MemoryStream xml = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitRes = outComp.Emit(pe, xmlDocumentationStream: xml);
        Assert.True(emitRes.Success, string.Join(Environment.NewLine, emitRes.Diagnostics));
        Assert.True(xml.Length > 0);

        // Verify generated union trees perform zero .Value accesses and zero boxing on [Union]/Nullable<Union>.
        List<string> generatedHits = FindUnionBoxingOrValueOps(outComp, t => t.Name is "Choice" or "Opt");
        Assert.Empty(generatedHits);

        // Verify generated [Union] struct compiles cleanly even with <ImplicitUsings>disable</ImplicitUsings> (zero global usings) and CS1591/CS1584/CS1658/CS1574 errors enabled.
        const string noImplicitSource = """
            #nullable enable
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace NoImplicitProbe;

            /// <summary>Manual case.</summary>
            public readonly struct ManualCase : ICborType
            {
                /// <summary>Raw bytes.</summary>
                public global::System.ReadOnlyMemory<byte> Raw { get; }
                /// <summary>Constr index.</summary>
                public int ConstrIndex => -1;
                /// <summary>Indefinite flag.</summary>
                public bool IsIndefinite => false;
                private ManualCase(global::System.ReadOnlyMemory<byte> raw) => Raw = raw;
                /// <summary>Reads a case.</summary>
                /// <param name="data">CBOR slice.</param>
                /// <param name="bytesConsumed">Bytes read.</param>
                public static ManualCase Read(global::System.ReadOnlyMemory<byte> data, out int bytesConsumed)
                {
                    SAIB.Cbor.Serialization.CborReader r = new(data.Span);
                    _ = r.ReadByteString();
                    bytesConsumed = data.Length - r.Buffer.Length;
                    return new(data[..bytesConsumed]);
                }
                /// <summary>Writes a case.</summary>
                /// <param name="output">Output buffer.</param>
                /// <param name="value">Value to write.</param>
                public static void Write(global::System.Buffers.IBufferWriter<byte> output, ManualCase value) =>
                    new SAIB.Cbor.Serialization.CborWriter(output).WriteByteString(value.Raw.Span);
            }

            /// <summary>Union compiled without global usings.</summary>
            [CborSerializable, CborUnion(typeof(ManualCase)), global::System.Runtime.CompilerServices.Union]
            public readonly partial struct NoImplicitChoice : ICborType;
            """;
        CSharpCompilation noImplComp = CSharpCompilation.Create("NoImplicitProbe", [CSharpSyntaxTree.ParseText(noImplicitSource, docParse)], References, docOpts);
        GeneratorDriverRunResult noImplRun = driver.RunGeneratorsAndUpdateCompilation(noImplComp, out Compilation noImplOut, out _).GetRunResult();
        Assert.Empty(noImplRun.Diagnostics);
        using MemoryStream peNoImpl = new();
        using MemoryStream xmlNoImpl = new();
        Microsoft.CodeAnalysis.Emit.EmitResult noImplEmit = noImplOut.Emit(peNoImpl, xmlDocumentationStream: xmlNoImpl);
        Assert.True(noImplEmit.Success, string.Join(Environment.NewLine, noImplEmit.Diagnostics));
        Assert.True(xmlNoImpl.Length > 0);

        // Positive self-test 1: undocumented public member triggers CS1591.
        CSharpCompilation missingDoc = CSharpCompilation.Create("MissingDoc",
            [CSharpSyntaxTree.ParseText("namespace D; public class Undoc { public int X => 1; }", docParse)], References, docOpts);
        using MemoryStream pe2 = new();
        using MemoryStream xml2 = new();
        Microsoft.CodeAnalysis.Emit.EmitResult failEmit = missingDoc.Emit(pe2, xmlDocumentationStream: xml2);
        Assert.Contains(failEmit.Diagnostics, d => d.Id == "CS1591");

        // Positive self-test 2: synthetic .Value, box Union, and box Nullable<Union> are all flagged.
        SyntaxTree probeBase = CSharpSyntaxTree.ParseText(source, docParse, "Base.cs");
        SyntaxTree probeTree = CSharpSyntaxTree.ParseText("""
            #nullable enable
            namespace DocAndOpProbe;

            public static class Violations
            {
                public static object? V1(Choice c) => c.Value;
                public static object V2(Choice c) => c;
                public static object? V3(Choice? c) => c;
            }
            """, docParse, "Violations.cs");
        CSharpCompilation violComp = CSharpCompilation.Create("ViolProbe", [probeBase, probeTree], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        _ = driver.RunGeneratorsAndUpdateCompilation(violComp, out Compilation violOut, out _);
        Assert.Empty(violOut.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        List<string> violHits = FindUnionBoxingOrValueOps(violOut, t => t.Name == "Choice", tree => tree.FilePath == "Violations.cs");
        Assert.Equal(3, violHits.Count);
    }

    private static List<string> FindUnionBoxingOrValueOps(Compilation compilation, Func<INamedTypeSymbol, bool> isUnion, Func<SyntaxTree, bool>? treeFilter = null)
    {
        static bool IsUnionOrNullable(ITypeSymbol? type, Func<INamedTypeSymbol, bool> match)
        {
            if (type is not INamedTypeSymbol named) { return false; }
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments[0] is INamedTypeSymbol inner)
            {
                return match(inner);
            }
            return match(named);
        }

        List<string> hits = [];
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (treeFilter is not null ? !treeFilter(tree) : !tree.FilePath.EndsWith(".Serializer.g.cs", StringComparison.Ordinal))
            {
                continue;
            }
            SemanticModel model = compilation.GetSemanticModel(tree);
            SyntaxNode root = tree.GetRoot();
            HashSet<IOperation> visited = [with(ReferenceEqualityComparer.Instance)];
            Queue<IOperation> queue = new();
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if (model.GetOperation(node) is { Parent: null } rootOp && visited.Add(rootOp))
                {
                    queue.Enqueue(rootOp);
                }
            }
            while (queue.Count > 0)
            {
                IOperation op = queue.Dequeue();
                foreach (IOperation child in op.ChildOperations)
                {
                    if (visited.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
                if (op is IPropertyReferenceOperation prop && prop.Property.Name == "Value"
                    && (IsUnionOrNullable(prop.Property.ContainingType, isUnion) || prop.Property.ContainingType.Name == "IUnion"))
                {
                    hits.Add("Value:" + op.Syntax);
                }
                else if (op is IConversionOperation conv
                    && Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetConversion(conv).IsBoxing
                    && IsUnionOrNullable(conv.Operand.Type, isUnion))
                {
                    hits.Add("Box:" + op.Syntax);
                }
            }
        }
        return hits;
    }

    [Fact]
    public void OfficialUnionIncrementalCachingAndCaseOrderInvarianceHold()
    {
        SyntaxTree root = ParseIncremental("Root.cs", """
            using System.Runtime.CompilerServices;
            namespace Snapshot;
            [CborSerializable, CborUnion(typeof(A), typeof(B)), Union] public readonly partial struct Choice : ICborType;
            [CborSerializable, CborList] public readonly partial record struct Standalone : ICborType { [CborOrder(0)] public partial int X { get; } }
            """);
        SyntaxTree a = ParseIncremental("A.cs", """
            namespace Snapshot;
            [CborSerializable, CborList, CborIndex(0)] public readonly partial record struct A : ICborType { [CborOrder(0)] public partial int Tag { get; } }
            """, includeGlobals: false);
        SyntaxTree b = ParseIncremental("B.cs", """
            namespace Snapshot;
            [CborSerializable, CborList, CborIndex(1)] public readonly partial record struct B : ICborType { [CborOrder(0)] public partial int Tag { get; } }
            """, includeGlobals: false);

        CSharpCompilation baseComp = IncrementalCompilation("OfficialCache", root, a, b);
        GeneratorDriver first = TrackingDriver().RunGenerators(baseComp);
        Assert.Equal(GeneratedText(first, "Snapshot.Choice"),
            GeneratedText(TrackingDriver().RunGenerators(IncrementalCompilation("OfficialCache", b, root, a)), "Snapshot.Choice"));

        SyntaxTree rootWithComment = root.WithChangedText(root.GetText().Replace(0, 0, "// comment\n"));
        GeneratorDriver afterComment = first.RunGenerators(baseComp.ReplaceSyntaxTree(root, rootWithComment));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(afterComment, "Snapshot.Choice"));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(afterComment, "Snapshot.A"));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(afterComment, "Snapshot.B"));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(afterComment, "Snapshot.Standalone"));

        SyntaxTree aChanged = ParseIncremental("A.cs", """
            namespace Snapshot;
            [CborSerializable, CborList, CborIndex(0)] public readonly partial record struct A : ICborType { [CborOrder(0)] public partial int Tag { get; } [CborOrder(1)] public partial int Extra { get; } }
            """, includeGlobals: false);
        GeneratorDriver afterCaseEdit = afterComment.RunGenerators(baseComp.ReplaceSyntaxTree(root, rootWithComment).ReplaceSyntaxTree(a, aChanged));
        Assert.Equal(IncrementalStepRunReason.Modified, SourceReason(afterCaseEdit, "Snapshot.Choice"));
        Assert.Equal(IncrementalStepRunReason.Cached, SourceReason(afterCaseEdit, "Snapshot.Standalone"));
    }

    [Fact]
    public void OutOfOrderCborOrderRecordRoundTripsInParameterOrder()
    {
        const string source = """
            global using System;
            using System.Buffers;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace Snapshot;
            [CborSerializable, CborList]
            public partial record Swap([CborOrder(1)] int A, [CborOrder(0)] int B) : CborRecord;
            public static class Probe
            {
                public static (int A, int B, byte[] Wire) RoundTrip(byte[] input)
                {
                    Swap model = Swap.Read(input);
                    Swap fresh = model with { Raw = default };
                    ArrayBufferWriter<byte> output = new();
                    Swap.Write(output, fresh);
                    return (model.A, model.B, output.WrittenSpan.ToArray());
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("SwapProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = output.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        Type probe = Assembly.Load(image.ToArray()).GetType("Snapshot.Probe", throwOnError: true)!;
        Func<byte[], (int A, int B, byte[] Wire)> roundTrip =
            probe.GetMethod("RoundTrip")!.CreateDelegate<Func<byte[], (int A, int B, byte[] Wire)>>();
        byte[] bytes = [0x82, 0x01, 0x02];
        (int a, int b, byte[] wire) = roundTrip(bytes);
        Assert.Equal(1, a);
        Assert.Equal(2, b);
        Assert.Equal(bytes, wire);
    }

    [Fact]
    public void SinglePartOutOfOrderUnionCasesDriveOverlapAndSecondaryProbeDispatch()
    {
        // Single-part types keep declaration order even when [CborOrder] disagrees (PartialWire.cs:36).
        // TupleShapesOverlap must read that same declaration-order member list: O1 [int, string] overlaps O2 [int, string],
        // whereas O1 [int, string] does not overlap O3 [string, int] even though O1's [CborOrder] says [string, int].
        const string overlapSource = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace Probe;
            [CborSerializable, CborUnion(typeof(O1), typeof(O2)), Union]
            public readonly partial struct Overlap : ICborType;
            [CborSerializable, CborUnion(typeof(O1), typeof(O3)), Union]
            public readonly partial struct Disjoint : ICborType;
            [CborSerializable, CborList]
            public readonly partial record struct O1 : ICborType
            {
                [CborOrder(1)] public partial int Second { get; }
                [CborOrder(0)] public partial string First { get; }
            }
            [CborSerializable, CborList]
            public readonly partial record struct O2 : ICborType
            {
                [CborOrder(0)] public partial int First { get; }
                [CborOrder(1)] public partial string Second { get; }
            }
            [CborSerializable, CborList]
            public readonly partial record struct O3 : ICborType
            {
                [CborOrder(0)] public partial string First { get; }
                [CborOrder(1)] public partial int Second { get; }
            }
            """;
        GeneratorDriverRunResult overlapRes = CreateDriver().RunGenerators(CreateCompilation("OverlapProbe", overlapSource)).GetRunResult();
        Diagnostic overlapDiag = Assert.Single(overlapRes.Diagnostics, d => d.Id == "CHCB002");
        Assert.Contains("Overlap", overlapDiag.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        const string probeSource = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace Probe;
            [CborSerializable, CborUnion(typeof(S1), typeof(S2)), Union]
            public readonly partial struct Disambiguated : ICborType;
            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct S1 : ICborType
            {
                [CborOrder(1)] public partial int Tag { get; }
                [CborOrder(0)] public partial int Num { get; }
            }
            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct S2 : ICborType
            {
                [CborOrder(1)] public partial int Tag { get; }
                [CborOrder(0)] public partial string Text { get; }
            }
            """;
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            CreateCompilation("SecondaryProbe", probeSource), out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        string gen = GeneratedText(driver, "Probe.Disambiguated");
        Assert.Contains("_ResolveIdx", gen, StringComparison.Ordinal);

        Assembly asm = Assembly.Load(image.ToArray());
        Type unionType = asm.GetType("Probe.Disambiguated", true)!;
        MethodInfo read = unionType.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
        IUnion u1 = (IUnion)read.Invoke(null, [new ReadOnlyMemory<byte>([0x82, 0x00, 0x05])])!;
        IUnion u2 = (IUnion)read.Invoke(null, [new ReadOnlyMemory<byte>([0x82, 0x00, 0x61, 0x61])])!;
        Assert.Equal("Probe.S1", u1.Value!.GetType().FullName);
        Assert.Equal("Probe.S2", u2.Value!.GetType().FullName);

        object s1 = asm.GetType("Probe.S1", true)!.GetMethod("Create")!.Invoke(null, [0, 5])!;
        object s2 = asm.GetType("Probe.S2", true)!.GetMethod("Create")!.Invoke(null, [0, "a"])!;
        Assert.Equal(new byte[] { 0x82, 0x00, 0x05 }, ((ICborType)s1).Raw.ToArray());
        Assert.Equal(new byte[] { 0x82, 0x00, 0x61, 0x61 }, ((ICborType)s2).Raw.ToArray());

        Type s1Type = asm.GetType("Probe.S1", true)!;
        Type s2Type = asm.GetType("Probe.S2", true)!;
        MethodInfo s1Read = s1Type.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
        MethodInfo s2Read = s2Type.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
        ConstructorInfo ctorS1 = unionType.GetConstructor([s1Type])!;
        ConstructorInfo ctorS2 = unionType.GetConstructor([s2Type])!;

        MethodInfo write = unionType.GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!;

        foreach (byte[] wrongS1 in (byte[][])[[0x82, 0x00, 0x61, 0x61], [0x9F, 0x00, 0x61, 0x61, 0xFF]])
        {
            object lazyS1 = s1Read.Invoke(null, [new ReadOnlyMemory<byte>(wrongS1)])!;
            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() => ctorS1.Invoke([lazyS1]));
            _ = Assert.IsType<CborValidationException>(ex.InnerException);
        }
        foreach (byte[] wrongS2 in (byte[][])[[0x82, 0x00, 0x05], [0x9F, 0x00, 0x05, 0xFF]])
        {
            object lazyS2 = s2Read.Invoke(null, [new ReadOnlyMemory<byte>(wrongS2)])!;
            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(() => ctorS2.Invoke([lazyS2]));
            _ = Assert.IsType<CborValidationException>(ex.InnerException);
        }

        (ConstructorInfo Ctor, object CaseVal, byte[] Wire, string TypeName)[] roundTrips =
        [
            (ctorS1, s1, [0x82, 0x00, 0x05], "Probe.S1"),
            (ctorS2, s2, [0x82, 0x00, 0x61, 0x61], "Probe.S2"),
            (ctorS1, s1Read.Invoke(null, [new ReadOnlyMemory<byte>([0x9F, 0x00, 0x05, 0xFF])])!, [0x9F, 0x00, 0x05, 0xFF], "Probe.S1"),
            (ctorS2, s2Read.Invoke(null, [new ReadOnlyMemory<byte>([0x9F, 0x00, 0x61, 0x61, 0xFF])])!, [0x9F, 0x00, 0x61, 0x61, 0xFF], "Probe.S2")
        ];
        foreach ((ConstructorInfo ctor, object caseVal, byte[] expectedWire, string expectedType) in roundTrips)
        {
            object unionVal = ctor.Invoke([caseVal]);
            System.Buffers.ArrayBufferWriter<byte> buf = new();
            _ = write.Invoke(null, [buf, unionVal]);
            byte[] serialized = buf.WrittenSpan.ToArray();
            Assert.Equal(expectedWire, serialized);
            IUnion roundTrip = (IUnion)read.Invoke(null, [new ReadOnlyMemory<byte>(serialized)])!;
            Assert.Equal(expectedType, roundTrip.Value!.GetType().FullName);
        }
    }

    [Fact]
    public void OneCaseAndTaggedIndexedStructUnionsRejectWrongDiscriminant()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace DiscriminantProbe;
            [CborSerializable, CborList, CborIndex(2)]
            public readonly partial record struct OnlyTwo : ICborType { [CborOrder(0)] public partial int Tag { get; } }
            [CborSerializable, CborUnion(typeof(OnlyTwo)), Union]
            public readonly partial struct SingleUnion : ICborType;

            [CborSerializable, CborList, CborTag(99), CborIndex(1)]
            public readonly partial record struct TaggedIndexed : ICborType { [CborOrder(0)] public partial int Tag { get; } }
            [CborSerializable, CborUnion(typeof(TaggedIndexed)), Union]
            public readonly partial struct TaggedUnion : ICborType;
            """;
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(
            CreateCompilation("DiscriminantProbe", source), out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        Assembly asm = Assembly.Load(image.ToArray());
        MethodInfo singleRead = asm.GetType("DiscriminantProbe.SingleUnion", true)!.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
        _ = singleRead.Invoke(null, [new ReadOnlyMemory<byte>([0x81, 0x02])]);
        TargetInvocationException singleErr = Assert.Throws<TargetInvocationException>(() =>
            singleRead.Invoke(null, [new ReadOnlyMemory<byte>([0x81, 0x03])]));
        _ = Assert.IsType<CborDecodeException>(singleErr.InnerException);

        MethodInfo taggedRead = asm.GetType("DiscriminantProbe.TaggedUnion", true)!.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
        _ = taggedRead.Invoke(null, [new ReadOnlyMemory<byte>(Convert.FromHexString("D8638101"))]);
        TargetInvocationException taggedErr = Assert.Throws<TargetInvocationException>(() =>
            taggedRead.Invoke(null, [new ReadOnlyMemory<byte>(Convert.FromHexString("D8638100"))]));
        _ = Assert.IsType<CborDecodeException>(taggedErr.InnerException);
    }

    [Fact]
    public void EagerNullablePropertyConsumesCborUndefined0xF7()
    {
        const string source = """
            global using System;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace Snapshot;
            [CborSerializable, CborList]
            public partial record EagerNullablePair([CborOrder(0)] int? First, [CborOrder(1)] int Second) : CborRecord;
            public static class Probe
            {
                public static (int? First, int Second) ReadPair(byte[] input)
                {
                    EagerNullablePair model = EagerNullablePair.Read(input);
                    return (model.First, model.Second);
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("UndefinedProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = output.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        Type probe = Assembly.Load(image.ToArray()).GetType("Snapshot.Probe", throwOnError: true)!;
        Func<byte[], (int? First, int Second)> readPair =
            probe.GetMethod("ReadPair")!.CreateDelegate<Func<byte[], (int? First, int Second)>>();
        (int? first, int second) = readPair([0x82, 0xF7, 0x05]);
        Assert.Null(first);
        Assert.Equal(5, second);
    }

    [Fact]
    public void MapAndListNullableTypeofDoesNotEmitCS8639()
    {
        const string source = """
            #nullable enable
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace Snapshot;
            [CborSerializable]
            public readonly partial record struct Box<T> : ICborType
            {
                public partial T Value { get; }
            }
            [CborSerializable, CborMap]
            public readonly partial record struct NullableCollections : ICborType
            {
                [CborProperty(0)] public partial CborList<string?> Texts { get; }
                [CborProperty(1)] public partial List<string?> RawTexts { get; }
                [CborProperty(2)] public partial Box<string?> BoxedText { get; }
                [CborProperty(3)] public partial CborMap<string, string?> MapTexts { get; }
            }
            """;
        CSharpCompilation input = CreateCompilation("NullableTypeofProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Diagnostic[] errors = [.. output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void OfficialStructUnionEmitsUnionMembersAndCompiles()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace Snapshot;
            [CborSerializable, CborUnion(typeof(First), typeof(Second)), Union]
            public readonly partial struct Choice : ICborType;
            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct First : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial int Value { get; }
            }
            [CborSerializable]
            public readonly partial record struct Second : ICborType
            {
                public partial string Text { get; }
            }
            """;
        CSharpCompilation input = CreateCompilation("OfficialUnionProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Diagnostic[] errors = [.. output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        Assert.Contains(driver.GetRunResult().Results.Single().GeneratedSources,
            s => s.HintName == "Snapshot.Choice.Serializer.g.cs");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedContainerUnionAllowsNonNegativeWhenSiblingIsNotPlainUnsigned(bool useTaggedSibling)
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace SignedSiblingParity;

            [CborSerializable]
            public readonly partial record struct SignedBox : ICborType { public partial long Value { get; } }

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct IndexedUIntList : ICborType
            {
                [CborOrder(0)] public partial uint Tag { get; }
            }

            [CborSerializable, CborTag(258)]
            public readonly partial record struct TaggedULong : ICborType { public partial ulong Value { get; } }

            [CborSerializable, CborUnion(typeof(SignedBox), typeof(IndexedUIntList)), Union]
            public readonly partial struct UnionWithListSibling : ICborType;

            [CborSerializable, CborUnion(typeof(SignedBox), typeof(TaggedULong)), Union]
            public readonly partial struct UnionWithTaggedSibling : ICborType;

            public static class Probe
            {
                public static void ExerciseListSibling()
                {
                    byte[] posFive = [0x05];
                    byte[] negOne = [0x20];

                    UnionWithListSibling rPos = UnionWithListSibling.Read(posFive);
                    if (rPos is not SignedBox { Value: 5 })
                        throw new InvalidOperationException("List sibling Read([0x05]) failed.");

                    UnionWithListSibling cPos = new(SignedBox.Create(5));
                    byte[] wPos = CborSerializer.Serialize(cPos);
                    if (!wPos.AsSpan().SequenceEqual(posFive) || UnionWithListSibling.Read(wPos) is not SignedBox { Value: 5 })
                        throw new InvalidOperationException("List sibling ctor/round-trip(5) failed.");

                    UnionWithListSibling cNeg = new(SignedBox.Create(-1));
                    if (!CborSerializer.Serialize(cNeg).AsSpan().SequenceEqual(negOne))
                        throw new InvalidOperationException("List sibling (-1) failed.");

                    if (UnionWithListSibling.Read((byte[])[0x81, 0x00]) is not IndexedUIntList { Tag: 0 })
                        throw new InvalidOperationException("List sibling IndexedUIntList failed.");
                }

                public static void ExerciseTaggedSibling()
                {
                    byte[] posFive = [0x05];
                    byte[] negOne = [0x20];

                    UnionWithTaggedSibling rPos = UnionWithTaggedSibling.Read(posFive);
                    if (rPos is not SignedBox { Value: 5 })
                        throw new InvalidOperationException("Tagged sibling Read([0x05]) failed.");

                    UnionWithTaggedSibling cPos = new(SignedBox.Create(5));
                    byte[] wPos = CborSerializer.Serialize(cPos);
                    if (!wPos.AsSpan().SequenceEqual(posFive) || UnionWithTaggedSibling.Read(wPos) is not SignedBox { Value: 5 })
                        throw new InvalidOperationException("Tagged sibling ctor/round-trip(5) failed.");

                    UnionWithTaggedSibling cNeg = new(SignedBox.Create(-1));
                    if (!CborSerializer.Serialize(cNeg).AsSpan().SequenceEqual(negOne))
                        throw new InvalidOperationException("Tagged sibling (-1) failed.");

                    byte[] taggedWire = Convert.FromHexString("D9010205");
                    if (UnionWithTaggedSibling.Read(taggedWire) is not TaggedULong { Value: 5 })
                        throw new InvalidOperationException("Tagged sibling TaggedULong failed.");
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("SignedSiblingParity", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        Type probe = Assembly.Load(image.ToArray()).GetType("SignedSiblingParity.Probe", throwOnError: true)!;
        string methodName = useTaggedSibling ? "ExerciseTaggedSibling" : "ExerciseListSibling";
        _ = probe.GetMethod(methodName)!.Invoke(null, null);
    }

    [Fact]
    public void StructuralTupleFallbackEnforcesCaseShapeOnConstructionAndRead()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace TupleFallbackProbe;

            [CborSerializable, CborList]
            public readonly partial record struct PairInt : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial int B { get; }
            }

            [CborSerializable, CborList]
            public readonly partial record struct PairText : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial string B { get; }
            }

            [CborSerializable, CborUnion(typeof(PairInt), typeof(PairText)), Union]
            public readonly partial struct TuplePair : ICborType;

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct NestedInt : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial PairInt Pair { get; }
            }

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct NestedText : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial PairText Pair { get; }
            }

            [CborSerializable, CborUnion(typeof(NestedInt), typeof(NestedText)), Union]
            public readonly partial struct NestedChoice : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] defInt = [0x82, 0x01, 0x02];
                    byte[] defText = [0x82, 0x01, 0x61, 0x61];
                    byte[] indefInt = [0x9F, 0x01, 0x02, 0xFF];
                    byte[] indefText = [0x9F, 0x01, 0x61, 0x61, 0xFF];

                    foreach (byte[] badInt in new byte[][] { defText, indefText })
                    {
                        try { _ = new TuplePair(PairInt.Read(badInt)); throw new InvalidOperationException("PairInt cross-case ctor must fail."); }
                        catch (CborValidationException) { }
                    }
                    foreach (byte[] badText in new byte[][] { defInt, indefInt })
                    {
                        try { _ = new TuplePair(PairText.Read(badText)); throw new InvalidOperationException("PairText cross-case ctor must fail."); }
                        catch (CborValidationException) { }
                    }

                    foreach (byte[] wire in new byte[][] { defInt, indefInt })
                    {
                        if (TuplePair.Read(wire) is not PairInt p) throw new InvalidOperationException("Expected PairInt.");
                        TuplePair rebuilt = new(p);
                        byte[] encoded = CborSerializer.Serialize(rebuilt);
                        if (!encoded.AsSpan().SequenceEqual(wire) || TuplePair.Read(encoded) is not PairInt { A: 1, B: 2 })
                            throw new InvalidOperationException("PairInt round-trip failed.");
                    }
                    foreach (byte[] wire in new byte[][] { defText, indefText })
                    {
                        if (TuplePair.Read(wire) is not PairText p) throw new InvalidOperationException("Expected PairText.");
                        TuplePair rebuilt = new(p);
                        byte[] encoded = CborSerializer.Serialize(rebuilt);
                        if (!encoded.AsSpan().SequenceEqual(wire) || TuplePair.Read(encoded) is not PairText { A: 1, B: "a" })
                            throw new InvalidOperationException("PairText round-trip failed.");
                    }

                    TuplePair createdInt = new(PairInt.Create(1, 2));
                    if (!CborSerializer.Serialize(createdInt).AsSpan().SequenceEqual(defInt))
                        throw new InvalidOperationException("PairInt Create round-trip failed.");
                    TuplePair createdText = new(PairText.Create(1, "a"));
                    if (!CborSerializer.Serialize(createdText).AsSpan().SequenceEqual(defText))
                        throw new InvalidOperationException("PairText Create round-trip failed.");

                    byte[] nestedIntWire = [0x82, 0x00, 0x82, 0x01, 0x02];
                    byte[] nestedTextWire = [0x82, 0x00, 0x82, 0x01, 0x61, 0x61];
                    try { _ = new NestedChoice(NestedInt.Read(nestedTextWire)); throw new InvalidOperationException("NestedInt cross-case ctor must fail."); }
                    catch (CborValidationException) { }
                    try { _ = new NestedChoice(NestedText.Read(nestedIntWire)); throw new InvalidOperationException("NestedText cross-case ctor must fail."); }
                    catch (CborValidationException) { }

                    if (NestedChoice.Read(nestedIntWire) is not NestedInt ni || NestedChoice.Read(CborSerializer.Serialize(new NestedChoice(ni))) is not NestedInt)
                        throw new InvalidOperationException("NestedInt round-trip failed.");
                    if (NestedChoice.Read(nestedTextWire) is not NestedText nt || NestedChoice.Read(CborSerializer.Serialize(new NestedChoice(nt))) is not NestedText)
                        throw new InvalidOperationException("NestedText round-trip failed.");
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("TupleFallbackProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        _ = Assembly.Load(image.ToArray()).GetType("TupleFallbackProbe.Probe", true)!.GetMethod("Exercise")!.Invoke(null, null);
    }

    [Fact]
    public void OuterProbeParityRejectsUnregisteredRootTagsOnReadAndConstruction()
    {
        const string source = """
            #nullable enable
            global using System;
            global using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace OuterProbe;

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct First : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial int Value { get; }
            }

            [CborSerializable]
            public readonly partial record struct Second : ICborType
            {
                public partial string Text { get; }
            }

            [CborSerializable, CborUnion(typeof(First), typeof(Second)), Union]
            public readonly partial struct Choice : ICborType;

            [CborSerializable, CborUnion(typeof(Second)), Union]
            public readonly partial struct SingleTextUnion : ICborType;

            [CborSerializable, CborMap]
            public readonly partial record struct MapCase : ICborType
            {
                [CborProperty(0)] public partial int Value { get; }
            }

            [CborSerializable, CborUnion(typeof(MapCase)), Union]
            public readonly partial struct SingleMapUnion : ICborType;

            [CborSerializable, CborTag(5)]
            public readonly partial record struct TaggedText : ICborType
            {
                public partial string Text { get; }
            }

            [CborSerializable, CborUnion(typeof(TaggedText)), Union]
            public readonly partial struct SingleTaggedUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] plainText = [0x61, 0x61];
                    byte[] taggedText = [0xC1, 0x61, 0x61];
                    byte[] tag5Text = [0xC5, 0x61, 0x61];
                    byte[] plainMap = [0xA1, 0x00, 0x01];
                    byte[] taggedMap = [0xC1, 0xA1, 0x00, 0x01];

                    // Standalone Second retains tag tolerance.
                    Second standaloneTagged = Second.Read(taggedText);
                    if (standaloneTagged.Text != "a" || !standaloneTagged.Raw.Span.SequenceEqual(taggedText))
                        throw new InvalidOperationException("Standalone Second.Read tag tolerance broken.");

                    // Plain text round-trips cleanly for both Choice and SingleTextUnion.
                    if (Choice.Read(plainText) is not Second sChoice || !CborSerializer.Serialize(new Choice(sChoice)).AsSpan().SequenceEqual(plainText) || Choice.Read(plainText) is not Second { Text: "a" })
                        throw new InvalidOperationException("Choice plain text round-trip failed.");
                    if (SingleTextUnion.Read(plainText) is not Second sSingle || !CborSerializer.Serialize(new SingleTextUnion(sSingle)).AsSpan().SequenceEqual(plainText) || SingleTextUnion.Read(plainText) is not Second { Text: "a" })
                        throw new InvalidOperationException("SingleTextUnion plain text round-trip failed.");

                    // Tag-wrapped text must fail BOTH Read (CborDecodeException) and ctor (CborValidationException).
                    try { _ = Choice.Read(taggedText); throw new InvalidOperationException("Choice.Read must reject tagged text."); }
                    catch (CborDecodeException) { }
                    try { _ = new Choice(standaloneTagged); throw new InvalidOperationException("new Choice must reject tagged text."); }
                    catch (CborValidationException) { }
                    try { _ = SingleTextUnion.Read(taggedText); throw new InvalidOperationException("SingleTextUnion.Read must reject tagged text."); }
                    catch (CborDecodeException) { }
                    try { _ = new SingleTextUnion(standaloneTagged); throw new InvalidOperationException("new SingleTextUnion must reject tagged text."); }
                    catch (CborValidationException) { }

                    // 1-case untagged map rejects tag-wrapped map on both Read and ctor.
                    if (SingleMapUnion.Read(plainMap) is not MapCase mc || !CborSerializer.Serialize(new SingleMapUnion(mc)).AsSpan().SequenceEqual(plainMap))
                        throw new InvalidOperationException("SingleMapUnion plain map failed.");
                    try { _ = SingleMapUnion.Read(taggedMap); throw new InvalidOperationException("SingleMapUnion.Read must reject tagged map."); }
                    catch (CborDecodeException) { }
                    try { _ = new SingleMapUnion(MapCase.Read(taggedMap)); throw new InvalidOperationException("new SingleMapUnion must reject tagged map."); }
                    catch (CborValidationException) { }

                    // 1-case tagged union requires tag 5 on both Read and ctor.
                    if (SingleTaggedUnion.Read(tag5Text) is not TaggedText tt || !CborSerializer.Serialize(new SingleTaggedUnion(tt)).AsSpan().SequenceEqual(tag5Text))
                        throw new InvalidOperationException("SingleTaggedUnion tag 5 failed.");
                    try { _ = SingleTaggedUnion.Read(taggedText); throw new InvalidOperationException("SingleTaggedUnion.Read must reject tag 1."); }
                    catch (CborDecodeException) { }
                    try { _ = SingleTaggedUnion.Read(plainText); throw new InvalidOperationException("SingleTaggedUnion.Read must reject untagged."); }
                    catch (CborDecodeException) { }
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("OuterProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        _ = Assembly.Load(image.ToArray()).GetType("OuterProbe.Probe", true)!.GetMethod("Exercise")!.Invoke(null, null);
    }


    [Fact]
    public void PureUnindexedTupleUnionSupportsIndefiniteArrays()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace PureTupleProbe;

            [CborSerializable, CborList]
            public readonly partial record struct T1 : ICborType { [CborOrder(0)] public partial int A { get; } }

            [CborSerializable, CborList]
            public readonly partial record struct T2 : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial int B { get; }
            }

            [CborSerializable, CborUnion(typeof(T1), typeof(T2)), Union]
            public readonly partial struct PureTupleUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] def1 = [0x81, 0x01];
                    byte[] def2 = [0x82, 0x01, 0x02];
                    byte[] indef1 = [0x9F, 0x01, 0xFF];
                    byte[] indef2 = [0x9F, 0x01, 0x02, 0xFF];

                    foreach (byte[] wire in new byte[][] { def1, indef1 })
                    {
                        if (PureTupleUnion.Read(wire) is not T1 t1 || !CborSerializer.Serialize(new PureTupleUnion(t1)).AsSpan().SequenceEqual(wire) || PureTupleUnion.Read(wire) is not T1 { A: 1 })
                            throw new InvalidOperationException("PureTupleUnion T1 failed.");
                    }
                    foreach (byte[] wire in new byte[][] { def2, indef2 })
                    {
                        if (PureTupleUnion.Read(wire) is not T2 t2 || !CborSerializer.Serialize(new PureTupleUnion(t2)).AsSpan().SequenceEqual(wire) || PureTupleUnion.Read(wire) is not T2 { A: 1, B: 2 })
                            throw new InvalidOperationException("PureTupleUnion T2 failed.");
                    }
                    foreach (byte[] bad in new byte[][] { [0x80], [0x9F, 0xFF], [0x83, 0x01, 0x02, 0x03], [0x9F, 0x01, 0x02, 0x03, 0xFF], [0x9F, 0x01, 0x02], [0xC1, 0x82, 0x01, 0x02] })
                    {
                        try { _ = PureTupleUnion.Read(bad); throw new InvalidOperationException("PureTupleUnion.Read must reject invalid input."); }
                        catch (CborDecodeException) { }
                    }
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("PureTupleProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        _ = Assembly.Load(image.ToArray()).GetType("PureTupleProbe.Probe", true)!.GetMethod("Exercise")!.Invoke(null, null);
    }
    [Fact]
    public void UnindexedTupleAndMixedTupleUnionsSupportIndefiniteArraysAndCompileCleanly()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace TupleDispatchProbe;

            [CborSerializable, CborList]
            public readonly partial record struct T1 : ICborType { [CborOrder(0)] public partial int A { get; } }

            [CborSerializable, CborList]
            public readonly partial record struct T2 : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial int B { get; }
            }

            [CborSerializable]
            public readonly partial record struct TextCase : ICborType { public partial string Text { get; } }

            [CborSerializable, CborTag(5)]
            public readonly partial record struct TaggedNum : ICborType { public partial int Num { get; } }

            [CborSerializable, CborList]
            public readonly partial record struct OptPair : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial int? B { get; }
            }

            [CborSerializable, CborList]
            public readonly partial record struct Triple : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial int B { get; }
                [CborOrder(2)] public partial int C { get; }
            }

            [CborSerializable, CborUnion(typeof(T1), typeof(T2)), Union]
            public readonly partial struct PureTupleUnion : ICborType;

            [CborSerializable, CborUnion(typeof(T1), typeof(T2), typeof(TextCase)), Union]
            public readonly partial struct MixedTupleUnion : ICborType;

            [CborSerializable, CborUnion(typeof(T1), typeof(T2), typeof(TextCase), typeof(TaggedNum)), Union]
            public readonly partial struct MixedTaggedTupleUnion : ICborType;

            [CborSerializable, CborUnion(typeof(OptPair), typeof(Triple), typeof(TextCase)), Union]
            public readonly partial struct NullableMixedTupleUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] def1 = [0x81, 0x01];
                    byte[] def2 = [0x82, 0x01, 0x02];
                    byte[] indef1 = [0x9F, 0x01, 0xFF];
                    byte[] indef2 = [0x9F, 0x01, 0x02, 0xFF];
                    byte[] def3 = [0x83, 0x01, 0x02, 0x03];
                    byte[] indef3 = [0x9F, 0x01, 0x02, 0x03, 0xFF];
                    byte[] textWire = [0x62, 0x68, 0x69];
                    byte[] taggedNumWire = [0xC5, 0x07];

                    foreach (byte[] wire in new byte[][] { def1, indef1 })
                    {
                        if (PureTupleUnion.Read(wire) is not T1 t1 || !CborSerializer.Serialize(new PureTupleUnion(t1)).AsSpan().SequenceEqual(wire) || PureTupleUnion.Read(wire) is not T1 { A: 1 })
                            throw new InvalidOperationException("PureTupleUnion T1 failed.");
                        if (MixedTupleUnion.Read(wire) is not T1 mt1 || !CborSerializer.Serialize(new MixedTupleUnion(mt1)).AsSpan().SequenceEqual(wire) || MixedTupleUnion.Read(wire) is not T1 { A: 1 })
                            throw new InvalidOperationException("MixedTupleUnion T1 failed.");
                        if (NullableMixedTupleUnion.Read(wire) is not OptPair op1 || !CborSerializer.Serialize(new NullableMixedTupleUnion(op1)).AsSpan().SequenceEqual(wire) || NullableMixedTupleUnion.Read(wire) is not OptPair { A: 1, B: null })
                            throw new InvalidOperationException("NullableMixedTupleUnion OptPair[1] failed.");
                    }
                    foreach (byte[] wire in new byte[][] { def2, indef2 })
                    {
                        if (PureTupleUnion.Read(wire) is not T2 t2 || !CborSerializer.Serialize(new PureTupleUnion(t2)).AsSpan().SequenceEqual(wire) || PureTupleUnion.Read(wire) is not T2 { A: 1, B: 2 })
                            throw new InvalidOperationException("PureTupleUnion T2 failed.");
                        if (MixedTupleUnion.Read(wire) is not T2 mt2 || !CborSerializer.Serialize(new MixedTupleUnion(mt2)).AsSpan().SequenceEqual(wire) || MixedTupleUnion.Read(wire) is not T2 { A: 1, B: 2 })
                            throw new InvalidOperationException("MixedTupleUnion T2 failed.");
                        if (NullableMixedTupleUnion.Read(wire) is not OptPair op2 || !CborSerializer.Serialize(new NullableMixedTupleUnion(op2)).AsSpan().SequenceEqual(wire) || NullableMixedTupleUnion.Read(wire) is not OptPair { A: 1, B: 2 })
                            throw new InvalidOperationException("NullableMixedTupleUnion OptPair[2] failed.");
                    }
                    foreach (byte[] wire in new byte[][] { def3, indef3 })
                    {
                        if (NullableMixedTupleUnion.Read(wire) is not Triple tr || !CborSerializer.Serialize(new NullableMixedTupleUnion(tr)).AsSpan().SequenceEqual(wire) || NullableMixedTupleUnion.Read(wire) is not Triple { A: 1, B: 2, C: 3 })
                            throw new InvalidOperationException("NullableMixedTupleUnion Triple failed.");
                    }

                    if (MixedTupleUnion.Read(textWire) is not TextCase tc || !CborSerializer.Serialize(new MixedTupleUnion(tc)).AsSpan().SequenceEqual(textWire) || MixedTupleUnion.Read(textWire) is not TextCase { Text: "hi" })
                        throw new InvalidOperationException("MixedTupleUnion TextCase failed.");
                    if (MixedTaggedTupleUnion.Read(indef1) is not T1 || MixedTaggedTupleUnion.Read(def2) is not T2 || MixedTaggedTupleUnion.Read(textWire) is not TextCase || MixedTaggedTupleUnion.Read(taggedNumWire) is not TaggedNum { Num: 7 })
                        throw new InvalidOperationException("MixedTaggedTupleUnion valid cases failed.");
                    if (NullableMixedTupleUnion.Read(textWire) is not TextCase)
                        throw new InvalidOperationException("NullableMixedTupleUnion TextCase failed.");

                    byte[][] pureInvalid = [[0x80], [0x9F, 0xFF], def3, indef3, [0x9F, 0x01, 0x02], [0xC1, 0x82, 0x01, 0x02]];
                    foreach (byte[] bad in pureInvalid)
                    {
                        try { _ = PureTupleUnion.Read(bad); throw new InvalidOperationException("PureTupleUnion.Read must reject invalid input."); }
                        catch (CborDecodeException) { }
                        try { _ = MixedTupleUnion.Read(bad); throw new InvalidOperationException("MixedTupleUnion.Read must reject invalid input."); }
                        catch (CborDecodeException) { }
                    }

                    foreach (byte[] badMixed in new byte[][] { [0xC1, 0x62, 0x68, 0x69], [0xC1, 0x81, 0x01] })
                    {
                        try { _ = MixedTupleUnion.Read(badMixed); throw new InvalidOperationException("MixedTupleUnion must reject tagged input."); }
                        catch (CborDecodeException) { }
                        try { _ = MixedTaggedTupleUnion.Read(badMixed); throw new InvalidOperationException("MixedTaggedTupleUnion must reject wrong tag."); }
                        catch (CborDecodeException) { }
                    }

                    foreach (byte[] badNullable in new byte[][] { [0x80], [0x9F, 0xFF], [0x84, 0x01, 0x02, 0x03, 0x04], [0x9F, 0x01, 0x02, 0x03, 0x04, 0xFF], [0x9F, 0x01, 0x02] })
                    {
                        try { _ = NullableMixedTupleUnion.Read(badNullable); throw new InvalidOperationException("NullableMixedTupleUnion must reject invalid input."); }
                        catch (CborDecodeException) { }
                    }
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("TupleDispatchProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        _ = Assembly.Load(image.ToArray()).GetType("TupleDispatchProbe.Probe", true)!.GetMethod("Exercise")!.Invoke(null, null);
    }

    [Fact]
    public void ExplicitInterfaceRawManualCaseCompilesAndRoundTrips()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Buffers;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace ExplicitRawProbe;

            public readonly struct ExtExplicitRawManual : ICborType
            {
                private readonly ReadOnlyMemory<byte> _raw;
                private readonly byte _payload;
                public ExtExplicitRawManual(byte payload) { _raw = default; _payload = payload; }
                private ExtExplicitRawManual(ReadOnlyMemory<byte> raw, byte payload) { _raw = raw; _payload = payload; }
                ReadOnlyMemory<byte> ICborType.Raw => _raw;
                int ICborType.ConstrIndex => -1;
                bool ICborType.IsIndefinite => false;
                public byte Payload => _payload;
                public static ExtExplicitRawManual Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
                {
                    SAIB.Cbor.Serialization.CborReader r = new(data.Span);
                    ReadOnlySpan<byte> bs = r.ReadByteString();
                    bytesConsumed = data.Length - r.Buffer.Length;
                    return new(data[..bytesConsumed], bs.IsEmpty ? (byte)0 : bs[0]);
                }
                public static void Write(IBufferWriter<byte> output, ExtExplicitRawManual value) =>
                    new SAIB.Cbor.Serialization.CborWriter(output).WriteByteString(new byte[] { value._payload });
            }

            [CborSerializable, CborUnion(typeof(ExtExplicitRawManual)), Union]
            public readonly partial struct ExplicitManualUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] wireA = [0x41, 0xAA];
                    if (ExplicitManualUnion.Read(wireA) is not ExtExplicitRawManual m1 || m1.Payload != 0xAA)
                        throw new InvalidOperationException("Read explicit manual case failed.");
                    ExplicitManualUnion fromRead = new(m1);
                    if (!CborSerializer.Serialize(fromRead).AsSpan().SequenceEqual(wireA))
                        throw new InvalidOperationException("Round-trip from Read failed.");

                    ExplicitManualUnion fromFresh = new(new ExtExplicitRawManual(0xBB));
                    byte[] wireB = CborSerializer.Serialize(fromFresh);
                    if (!wireB.AsSpan().SequenceEqual((byte[])[0x41, 0xBB]) || ExplicitManualUnion.Read(wireB) is not ExtExplicitRawManual { Payload: 0xBB })
                        throw new InvalidOperationException("EncodeFresh explicit manual case failed.");
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("ExplicitRawProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        _ = Assembly.Load(image.ToArray()).GetType("ExplicitRawProbe.Probe", true)!.GetMethod("Exercise")!.Invoke(null, null);
    }

    [Fact]
    public void StaticConstrAndPlutusDataUnionCasesEnforceArrayArityOnReadAndConstruction()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace StaticConstrArityProbe;

            [PlutusData(0)]
            public readonly partial record struct PlutusOne : ICborType
            {
                [CborOrder(0)] public partial int Value { get; }
            }

            [CborSerializable, CborConstr(1)]
            public readonly partial record struct ConstrTwo : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial int B { get; }
            }

            [CborSerializable, CborConstr(2)]
            public readonly partial record struct OptBeforeReqConstr : ICborType
            {
                [CborOrder(0)] public partial int A { get; }
                [CborOrder(1)] public partial int? B { get; }
                [CborOrder(2)] public partial int C { get; }
            }

            [CborSerializable, CborConstr(3)]
            public partial record ZeroConstrRecord() : CborRecord;

            [CborSerializable, CborUnion(typeof(PlutusOne), typeof(ConstrTwo), typeof(OptBeforeReqConstr), typeof(ZeroConstrRecord)), Union]
            public readonly partial struct StaticConstrUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    foreach (byte[] ok1 in new byte[][] { [0xD8, 0x79, 0x81, 0x05], [0xD8, 0x79, 0x9F, 0x05, 0xFF] })
                    {
                        if (StaticConstrUnion.Read(ok1) is not PlutusOne { Value: 5 } u1 || !CborSerializer.Serialize(new StaticConstrUnion(u1)).AsSpan().SequenceEqual(ok1))
                            throw new InvalidOperationException("PlutusOne valid wire failed.");
                    }
                    byte[] bad1Indef = [0xD8, 0x79, 0x9F, 0xFF];
                    ExpectDecodeFail(() => StaticConstrUnion.Read(bad1Indef), "PlutusOne.Read empty indef");
                    PlutusOne raw1 = PlutusOne.Read(bad1Indef);
                    ExpectValidationFail(() => new StaticConstrUnion(raw1), "PlutusOne ctor empty indef");
                    ExpectDecodeFail(() => StaticConstrUnion.Read(new byte[] { 0xD8, 0x79, 0x80 }), "PlutusOne.Read empty def");
                    ExpectDecodeFail(() => StaticConstrUnion.Read(new byte[] { 0xD8, 0x79, 0x82, 0x05, 0x06 }), "PlutusOne.Read overlong");

                    foreach (byte[] ok2 in new byte[][] { [0xD8, 0x7A, 0x82, 0x01, 0x02], [0xD8, 0x7A, 0x9F, 0x01, 0x02, 0xFF] })
                    {
                        if (StaticConstrUnion.Read(ok2) is not ConstrTwo { A: 1, B: 2 } u2 || !CborSerializer.Serialize(new StaticConstrUnion(u2)).AsSpan().SequenceEqual(ok2))
                            throw new InvalidOperationException("ConstrTwo valid wire failed.");
                    }
                    byte[] bad2Indef = [0xD8, 0x7A, 0x9F, 0x01, 0xFF];
                    ExpectDecodeFail(() => StaticConstrUnion.Read(bad2Indef), "ConstrTwo.Read truncated indef");
                    ConstrTwo raw2 = ConstrTwo.Read(bad2Indef);
                    ExpectValidationFail(() => new StaticConstrUnion(raw2), "ConstrTwo ctor truncated indef");
                    ExpectDecodeFail(() => StaticConstrUnion.Read(new byte[] { 0xD8, 0x7A, 0x81, 0x01 }), "ConstrTwo.Read truncated def");

                    foreach (byte[] ok3 in new byte[][] { [0xD8, 0x7B, 0x83, 0x01, 0xF6, 0x03], [0xD8, 0x7B, 0x9F, 0x01, 0xF6, 0x03, 0xFF] })
                    {
                        if (StaticConstrUnion.Read(ok3) is not OptBeforeReqConstr { A: 1, B: null, C: 3 } u3 || !CborSerializer.Serialize(new StaticConstrUnion(u3)).AsSpan().SequenceEqual(ok3))
                            throw new InvalidOperationException("OptBeforeReqConstr valid wire failed.");
                    }
                    foreach (byte[] bad3 in new byte[][] { [0xD8, 0x7B, 0x82, 0x01, 0xF6], [0xD8, 0x7B, 0x9F, 0x01, 0xF6, 0xFF] })
                    {
                        ExpectDecodeFail(() => StaticConstrUnion.Read(bad3), "OptBeforeReqConstr.Read truncated");
                        OptBeforeReqConstr raw3 = OptBeforeReqConstr.Read(bad3);
                        ExpectValidationFail(() => new StaticConstrUnion(raw3), "OptBeforeReqConstr ctor truncated");
                    }

                    foreach (byte[] ok4 in new byte[][] { [0xD8, 0x7C, 0x80], [0xD8, 0x7C, 0x9F, 0xFF] })
                    {
                        if (StaticConstrUnion.Read(ok4) is not ZeroConstrRecord z || !CborSerializer.Serialize(new StaticConstrUnion(z)).AsSpan().SequenceEqual(ok4))
                            throw new InvalidOperationException("ZeroConstrRecord valid wire failed.");
                    }
                    if (!CborSerializer.Serialize(new StaticConstrUnion(new ZeroConstrRecord())).AsSpan().SequenceEqual(new byte[] { 0xD8, 0x7C, 0x80 }))
                        throw new InvalidOperationException("ZeroConstrRecord EncodeFresh failed.");
                    ZeroConstrRecord tampered = new() { Raw = new byte[] { 0xD8, 0x7C, 0x83, 0x01, 0x02, 0x03 } };
                    ExpectDecodeFail(() => StaticConstrUnion.Read(tampered.Raw), "ZeroConstrRecord.Read tampered");
                    ExpectValidationFail(() => new StaticConstrUnion(tampered), "ZeroConstrRecord ctor tampered");
                }

                private static void ExpectDecodeFail(Func<StaticConstrUnion> act, string label)
                {
                    try { _ = act(); throw new InvalidOperationException($"{label} should throw CborDecodeException."); }
                    catch (CborDecodeException) { }
                }

                private static void ExpectValidationFail(Func<StaticConstrUnion> act, string label)
                {
                    try { _ = act(); throw new InvalidOperationException($"{label} should throw CborValidationException."); }
                    catch (CborValidationException) { }
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("StaticConstrArityProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly.Load(image.ToArray()).GetType("StaticConstrArityProbe.Probe", true)!.GetMethod("Exercise")!.CreateDelegate<Action>()();
    }

    [Fact]
    public void DynamicConstrUnionExcludesSiblingClaimedTagsOnConstruction()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace MixedConstrProbe;

            [CborSerializable, CborConstr(0)]
            public readonly partial record struct StaticConstr0 : ICborType
            {
                [CborOrder(0)] public partial int Value { get; }
            }

            [CborSerializable, CborTag(200), CborList]
            public readonly partial record struct ExplicitTag200 : ICborType
            {
                [CborOrder(0)] public partial int Value { get; }
            }

            [CborSerializable, CborConstr]
            public readonly partial record struct DynConstr : ICborType
            {
                [CborOrder(0)] public partial CborList<int> Fields { get; }
            }

            [CborSerializable, CborUnion(typeof(StaticConstr0), typeof(ExplicitTag200), typeof(DynConstr)), Union]
            public readonly partial struct MixedConstrUnion : ICborType;

            [CborSerializable, CborUnion(typeof(StaticConstr0), typeof(ExplicitTag200)), Union]
            public readonly partial struct StaticOnlyTaggedUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    DynConstr claimedStatic = DynConstr.Create(0, CborList.Create<int>([42]));
                    try { _ = new MixedConstrUnion(claimedStatic); throw new InvalidOperationException("Dynamic constr ctor must reject sibling-claimed tag 121."); }
                    catch (CborValidationException) { }

                    DynConstr claimedTag200 = DynConstr.Read(new byte[] { 0xD8, 0xC8, 0x81, 0x18, 0x2A });
                    try { _ = new MixedConstrUnion(claimedTag200); throw new InvalidOperationException("Dynamic constr ctor must reject sibling-claimed tag 200."); }
                    catch (CborValidationException) { }

                    DynConstr unclaimed = DynConstr.Create(1, CborList.Create<int>([42]));
                    MixedConstrUnion built = new(unclaimed);
                    byte[] wire = CborSerializer.Serialize(built);
                    if (MixedConstrUnion.Read(wire) is not DynConstr back || !CborSerializer.Serialize(new MixedConstrUnion(back)).AsSpan().SequenceEqual(wire))
                        throw new InvalidOperationException("Unclaimed dynamic constr round-trip failed.");
                    if (!wire.AsSpan().SequenceEqual(new byte[] { 0xD8, 0x7A, 0x81, 0x18, 0x2A }))
                        throw new InvalidOperationException($"Unexpected dynamic constr bytes: {Convert.ToHexString(wire)}.");

                    MixedConstrUnion s0 = new(StaticConstr0.Create(7));
                    if (MixedConstrUnion.Read(CborSerializer.Serialize(s0)) is not StaticConstr0 { Value: 7 })
                        throw new InvalidOperationException("StaticConstr0 round-trip failed.");
                    MixedConstrUnion t200 = new(ExplicitTag200.Create(42));
                    if (MixedConstrUnion.Read(CborSerializer.Serialize(t200)) is not ExplicitTag200 { Value: 42 })
                        throw new InvalidOperationException("ExplicitTag200 round-trip failed.");

                    foreach (byte[] wideWire in new byte[][]
                    {
                        [0xDB, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x79, 0x81, 0x18, 0x2A],
                        [0xDB, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0xC8, 0x81, 0x18, 0x2A]
                    })
                    {
                        DynConstr dynWide = DynConstr.Read(wideWire);
                        MixedConstrUnion wideBuilt = new(dynWide);
                        if (MixedConstrUnion.Read(wideWire) is not DynConstr || !CborSerializer.Serialize(wideBuilt).AsSpan().SequenceEqual(wideWire))
                            throw new InvalidOperationException("Wide tag dynamic constr round-trip failed.");
                    }

                    byte[] staticWide200 = [0xDB, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0xC8, 0x81, 0x18, 0x2A];
                    try
                    {
                        _ = StaticOnlyTaggedUnion.Read(staticWide200);
                        throw new InvalidOperationException("StaticOnlyTaggedUnion.Read should reject wide tag 2^32 + 200.");
                    }
                    catch (CborDecodeException ex)
                    {
                        if (ex.Actual != "4294967496" || ex.Expected != "registered union tag" || ex.Offset != 0)
                            throw new InvalidOperationException($"Unexpected wide tag error: offset={ex.Offset}, expected={ex.Expected}, actual={ex.Actual}.");
                    }
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("MixedConstrProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly.Load(image.ToArray()).GetType("MixedConstrProbe.Probe", true)!.GetMethod("Exercise")!.CreateDelegate<Action>()();
    }

    [Fact]
    public void ClosedGenericErrorTypeArgumentEmitsCHCB006AndOpenGenericStaysValid()
    {
        const string badSource = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace ErrorGenericProbe;
            [CborSerializable] public readonly partial record struct Box<T> : ICborType { public partial T Value { get; } }
            [CborSerializable, CborUnion(typeof(Box<DoesNotExist>)), Union] public readonly partial struct BadDirect : ICborType;
            [CborSerializable, CborUnion(typeof(Box<Box<DoesNotExist>>)), Union] public readonly partial struct BadNested : ICborType;
            [CborSerializable, CborUnion(typeof(Box<DoesNotExist[]>)), Union] public readonly partial struct BadArray : ICborType;
            """;
        GeneratorDriverRunResult badResult = CreateDriver().RunGenerators(CreateCompilation("ErrorGenericProbe", badSource)).GetRunResult();
        foreach (string name in new[] { "BadDirect", "BadNested", "BadArray" })
        {
            Diagnostic diag = Assert.Single(badResult.Diagnostics, d => d.Id == "CHCB006" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains(name, StringComparison.Ordinal));
            Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
            Assert.True(diag.Location.IsInSource);
            Assert.DoesNotContain(badResult.Results.Single().GeneratedSources, s => s.HintName.Contains(name + ".", StringComparison.Ordinal));
        }

        const string goodOpenSource = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace OpenGenericProbe;
            [CborSerializable] public readonly partial record struct Box<T> : ICborType { public partial T Value { get; } }
            [CborSerializable, CborUnion(typeof(Box<>)), Union] public readonly partial struct GoodOpen<T> : ICborType;
            public static class Probe
            {
                public static void Exercise()
                {
                    GoodOpen<int> u = Box<int>.Create(9);
                    byte[] wire = CborSerializer.Serialize(u);
                    if (GoodOpen<int>.Read(wire) is not Box<int> { Value: 9 } || !wire.AsSpan().SequenceEqual(new byte[] { 0x09 }))
                        throw new InvalidOperationException("Valid open-generic union failed.");
                }
            }
            """;
        CSharpCompilation goodInput = CreateCompilation("OpenGenericProbe", goodOpenSource);
        GeneratorDriver goodDriver = CreateDriver().RunGeneratorsAndUpdateCompilation(goodInput, out Compilation goodOut, out _);
        Diagnostic[] goodErrors = [.. goodDriver.GetRunResult().Diagnostics.Concat(goodOut.GetDiagnostics()).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)];
        Assert.True(goodErrors.Length == 0, string.Join(Environment.NewLine, goodErrors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = goodOut.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly.Load(image.ToArray()).GetType("OpenGenericProbe.Probe", true)!.GetMethod("Exercise")!.CreateDelegate<Action>()();
    }

    [Fact]
    public void ExplicitlyTaggedConstrUnionCasesRoundTripAndValidateOuterAndInnerTags()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace TaggedConstrProbe;

            [CborTag(200), PlutusData(0)]
            public readonly partial record struct TaggedStaticConstr : ICborType
            {
                [CborOrder(0)] public partial int Value { get; }
            }

            [CborSerializable, CborTag(201), CborConstr]
            public readonly partial record struct TaggedDynConstr : ICborType
            {
                [CborOrder(0)] public partial CborList<int> Fields { get; }
            }

            [CborTag(200), PlutusData(0)]
            public partial record TaggedStaticRec([CborOrder(0)] int Value) : CborRecord;

            [CborSerializable, CborTag(201), CborConstr]
            public partial record TaggedDynRec([CborOrder(0)] CborList<int> Fields) : CborRecord;

            [PlutusData(0)]
            public readonly partial record struct PlainStatic0 : ICborType
            {
                [CborOrder(0)] public partial int Value { get; }
            }

            [CborSerializable, CborConstr]
            public readonly partial record struct PlainDynConstr : ICborType
            {
                [CborOrder(0)] public partial CborList<int> Fields { get; }
            }

            [CborSerializable, CborUnion(typeof(TaggedStaticConstr), typeof(TaggedDynConstr)), Union]
            public readonly partial struct TaggedConstrUnion : ICborType;

            [CborSerializable, CborUnion(typeof(TaggedStaticRec), typeof(TaggedDynRec)), Union]
            public readonly partial struct TaggedConstrRecUnion : ICborType;

            [CborSerializable, CborUnion(typeof(PlainStatic0), typeof(TaggedStaticConstr), typeof(TaggedDynConstr), typeof(PlainDynConstr)), Union]
            public readonly partial struct CoexistingTaggedConstrUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] okStaticDef = [0xD8, 0xC8, 0xD8, 0x79, 0x81, 0x05];
                    byte[] okStaticIndef = [0xD8, 0xC8, 0xD8, 0x79, 0x9F, 0x05, 0xFF];
                    byte[] okDyn = [0xD8, 0xC9, 0xD8, 0x79, 0x81, 0x05];
                    byte[] wrongOuter = [0xD8, 0x79, 0x81, 0x05];
                    byte[] wrongInner = [0xD8, 0xC8, 0xD8, 0x7A, 0x81, 0x05];
                    byte[] badArityIndef = [0xD8, 0xC8, 0xD8, 0x79, 0x9F, 0xFF];
                    byte[] badArityOver = [0xD8, 0xC8, 0xD8, 0x79, 0x82, 0x05, 0x06];
                    byte[] missingInner = [0xD8, 0xC9, 0x81, 0x05];
                    byte[] nonArrayInner = [0xD8, 0xC9, 0xD8, 0x79, 0x05];

                    foreach (byte[] wire in new[] { okStaticDef, okStaticIndef })
                    {
                        if (TaggedConstrUnion.Read(wire) is not TaggedStaticConstr { Value: 5 } s || !CborSerializer.Serialize(new TaggedConstrUnion(s)).AsSpan().SequenceEqual(wire))
                            throw new InvalidOperationException("TaggedStaticConstr wire round-trip failed.");
                        if (TaggedConstrRecUnion.Read(wire) is not TaggedStaticRec { Value: 5 } sr || !CborSerializer.Serialize(new TaggedConstrRecUnion(sr)).AsSpan().SequenceEqual(wire))
                            throw new InvalidOperationException("TaggedStaticRec wire round-trip failed.");
                    }
                    if (!CborSerializer.Serialize(new TaggedConstrUnion(TaggedStaticConstr.Create(5))).AsSpan().SequenceEqual(okStaticDef))
                        throw new InvalidOperationException("TaggedStaticConstr.Create ctor failed.");
                    if (!CborSerializer.Serialize(new TaggedConstrRecUnion(TaggedStaticRec.Create(5))).AsSpan().SequenceEqual(okStaticDef))
                        throw new InvalidOperationException("TaggedStaticRec.Create ctor failed.");

                    if (TaggedConstrUnion.Read(okDyn) is not TaggedDynConstr d || d.Fields.Count != 1 || !d.Fields.Raw.Span.SequenceEqual(new byte[] { 0x81, 0x05 })
                        || !CborSerializer.Serialize(new TaggedConstrUnion(d)).AsSpan().SequenceEqual(okDyn))
                        throw new InvalidOperationException("TaggedDynConstr Read round-trip failed.");
                    if (!CborSerializer.Serialize(new TaggedConstrUnion(TaggedDynConstr.Create(0, CborList.Create<int>([5])))).AsSpan().SequenceEqual(okDyn))
                        throw new InvalidOperationException("TaggedDynConstr.Create ctor failed.");
                    if (TaggedConstrRecUnion.Read(okDyn) is not TaggedDynRec dr || dr.Fields.Count != 1 || !dr.Fields.Raw.Span.SequenceEqual(new byte[] { 0x81, 0x05 })
                        || !CborSerializer.Serialize(new TaggedConstrRecUnion(dr)).AsSpan().SequenceEqual(okDyn))
                        throw new InvalidOperationException("TaggedDynRec Read round-trip failed.");
                    if (!CborSerializer.Serialize(new TaggedConstrRecUnion(TaggedDynRec.Create(0, CborList.Create<int>([5])))).AsSpan().SequenceEqual(okDyn))
                        throw new InvalidOperationException("TaggedDynRec.Create ctor failed.");

                    foreach (byte[] bad in new[] { wrongOuter, wrongInner, badArityIndef, badArityOver })
                    {
                        ExpectDecodeFail(() => _ = TaggedConstrUnion.Read(bad), "TaggedConstrUnion static Read");
                        ExpectDecodeFail(() => _ = TaggedConstrRecUnion.Read(bad), "TaggedConstrRecUnion static Read");
                        ExpectValidationFail(() => _ = new TaggedConstrRecUnion(new TaggedStaticRec(5) { Raw = bad }), "TaggedConstrRecUnion static ctor");
                    }
                    foreach (byte[] badArity in new[] { badArityIndef, badArityOver })
                    {
                        TaggedStaticConstr parsed = TaggedStaticConstr.Read(badArity);
                        ExpectValidationFail(() => _ = new TaggedConstrUnion(parsed), "TaggedConstrUnion static arity ctor");
                    }

                    foreach (byte[] bad in new[] { missingInner, nonArrayInner })
                    {
                        ExpectDecodeFail(() => _ = TaggedConstrUnion.Read(bad), "TaggedConstrUnion dyn Read");
                        ExpectDecodeFail(() => _ = TaggedConstrRecUnion.Read(bad), "TaggedConstrRecUnion dyn Read");
                        ExpectValidationFail(() => _ = new TaggedConstrRecUnion(new TaggedDynRec(CborList.Create<int>([5])) { Raw = bad }), "TaggedConstrRecUnion dyn ctor");
                    }

                    if (CoexistingTaggedConstrUnion.Read(wrongOuter) is not PlainStatic0 { Value: 5 })
                        throw new InvalidOperationException("Coexisting tag 121 failed.");
                    if (CoexistingTaggedConstrUnion.Read(okStaticDef) is not TaggedStaticConstr { Value: 5 })
                        throw new InvalidOperationException("Coexisting tag 200 failed.");
                    if (CoexistingTaggedConstrUnion.Read(okDyn) is not TaggedDynConstr)
                        throw new InvalidOperationException("Coexisting tag 201 failed.");
                    if (CoexistingTaggedConstrUnion.Read(new byte[] { 0xD8, 0x7A, 0x81, 0x05 }) is not PlainDynConstr)
                        throw new InvalidOperationException("Coexisting dynamic constr failed.");
                    ExpectValidationFail(() => _ = new CoexistingTaggedConstrUnion(PlainDynConstr.Read(new byte[] { 0xD8, 0xC9, 0x81, 0x05 })), "PlainDynConstr sibling tag 201 exclusion");
                }

                private static void ExpectDecodeFail(Action act, string label)
                {
                    try { act(); throw new InvalidOperationException($"{label} should throw CborDecodeException."); }
                    catch (CborDecodeException) { }
                }

                private static void ExpectValidationFail(Action act, string label)
                {
                    try { act(); throw new InvalidOperationException($"{label} should throw CborValidationException."); }
                    catch (CborValidationException) { }
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("TaggedConstrProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly.Load(image.ToArray()).GetType("TaggedConstrProbe.Probe", true)!.GetMethod("Exercise")!.CreateDelegate<Action>()();
    }

    [Fact]
    public void CollidingIndexUnionWithNullableSeparatingFieldRejectsTruncatedStreamRead()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace SecNullProbe;

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct SecNullA : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial string? Text { get; }
            }

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct SecNullB : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial int? Num { get; }
            }

            [CborSerializable, CborUnion(typeof(SecNullA), typeof(SecNullB)), Union]
            public readonly partial struct SecNullUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] truncText = [0x81, 0x00, 0x61, 0x61];
                    byte[] truncInt = [0x81, 0x00, 0x05];
                    byte[] truncSolo = [0x81, 0x00];

                    foreach (byte[] bad in new[] { truncText, truncInt, truncSolo })
                    {
                        try { _ = SecNullUnion.Read(bad, out _); throw new InvalidOperationException("Truncated SecNullUnion.Read should throw CborDecodeException."); }
                        catch (CborDecodeException) { }
                    }

                    try { _ = new SecNullUnion(SecNullA.Read(truncSolo)); throw new InvalidOperationException("SecNullA trunc ctor should fail."); }
                    catch (CborValidationException) { }
                    try { _ = new SecNullUnion(SecNullB.Read(truncSolo)); throw new InvalidOperationException("SecNullB trunc ctor should fail."); }
                    catch (CborValidationException) { }

                    foreach (byte[] okA in new byte[][] { [0x82, 0x00, 0x61, 0x61], [0x9F, 0x00, 0x61, 0x61, 0xFF] })
                    {
                        if (SecNullUnion.Read(okA) is not SecNullA { Tag: 0, Text: "a" } a || !CborSerializer.Serialize(new SecNullUnion(a)).AsSpan().SequenceEqual(okA))
                            throw new InvalidOperationException("SecNullA valid wire failed.");
                    }
                    foreach (byte[] okB in new byte[][] { [0x82, 0x00, 0x05], [0x9F, 0x00, 0x05, 0xFF] })
                    {
                        if (SecNullUnion.Read(okB) is not SecNullB { Tag: 0, Num: 5 } b || !CborSerializer.Serialize(new SecNullUnion(b)).AsSpan().SequenceEqual(okB))
                            throw new InvalidOperationException("SecNullB valid wire failed.");
                    }
                    if (!CborSerializer.Serialize(new SecNullUnion(SecNullA.Create(0, "a"))).AsSpan().SequenceEqual(new byte[] { 0x82, 0x00, 0x61, 0x61 }))
                        throw new InvalidOperationException("SecNullA.Create failed.");
                    if (!CborSerializer.Serialize(new SecNullUnion(SecNullB.Create(0, 5))).AsSpan().SequenceEqual(new byte[] { 0x82, 0x00, 0x05 }))
                        throw new InvalidOperationException("SecNullB.Create failed.");
                    if (SecNullUnion.Read(new byte[] { 0x82, 0x00, 0x61, 0x61, 0x05 }, out int consumedA) is not SecNullA { Tag: 0, Text: "a" } || consumedA != 4)
                        throw new InvalidOperationException("SecNullA streaming slice failed.");
                    if (SecNullUnion.Read(new byte[] { 0x82, 0x00, 0x05, 0x61, 0x61 }, out int consumedB) is not SecNullB { Tag: 0, Num: 5 } || consumedB != 3)
                        throw new InvalidOperationException("SecNullB streaming slice failed.");
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("SecNullProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly.Load(image.ToArray()).GetType("SecNullProbe.Probe", true)!.GetMethod("Exercise")!.CreateDelegate<Action>()();
    }

    [Fact]
    public void CollidingIndexUnionPrefersNonNullableLaterSeparatingField()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace LaterDiscProbe;

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct LaterDiscA : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial string? Label { get; }
                [CborOrder(2)] public partial int Count { get; }
            }

            [CborSerializable, CborList, CborIndex(0)]
            public readonly partial record struct LaterDiscB : ICborType
            {
                [CborOrder(0)] public partial int Tag { get; }
                [CborOrder(1)] public partial int? Count { get; }
                [CborOrder(2)] public partial string Label { get; }
            }

            [CborSerializable, CborUnion(typeof(LaterDiscA), typeof(LaterDiscB)), Union]
            public readonly partial struct LaterDiscUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] nullA = [0x83, 0x00, 0xF6, 0x07];
                    byte[] fullA = [0x83, 0x00, 0x61, 0x61, 0x07];
                    byte[] indefNullA = [0x9F, 0x00, 0xF6, 0x07, 0xFF];
                    byte[] nullB = [0x83, 0x00, 0xF6, 0x62, 0x68, 0x69];
                    byte[] fullB = [0x83, 0x00, 0x03, 0x62, 0x68, 0x69];
                    byte[] indefNullB = [0x9F, 0x00, 0xF6, 0x62, 0x68, 0x69, 0xFF];

                    foreach (byte[] wireA in new byte[][] { nullA, fullA, indefNullA })
                    {
                        if (LaterDiscUnion.Read(wireA) is not LaterDiscA { Tag: 0, Count: 7 } decodedA
                            || !CborSerializer.Serialize(new LaterDiscUnion(decodedA)).AsSpan().SequenceEqual(wireA)
                            || !CborSerializer.Serialize(new LaterDiscUnion(LaterDiscA.Read(wireA))).AsSpan().SequenceEqual(wireA))
                            throw new InvalidOperationException("LaterDiscA round-trip failed.");
                    }
                    if (!CborSerializer.Serialize(new LaterDiscUnion(LaterDiscA.Create(0, 7, "a"))).AsSpan().SequenceEqual(fullA))
                        throw new InvalidOperationException("LaterDiscA.Create round-trip failed.");

                    foreach (byte[] wireB in new byte[][] { nullB, fullB, indefNullB })
                    {
                        if (LaterDiscUnion.Read(wireB) is not LaterDiscB { Tag: 0, Label: "hi" } decodedB
                            || !CborSerializer.Serialize(new LaterDiscUnion(decodedB)).AsSpan().SequenceEqual(wireB)
                            || !CborSerializer.Serialize(new LaterDiscUnion(LaterDiscB.Read(wireB))).AsSpan().SequenceEqual(wireB))
                            throw new InvalidOperationException("LaterDiscB round-trip failed.");
                    }
                    if (!CborSerializer.Serialize(new LaterDiscUnion(LaterDiscB.Create(0, "hi", 3))).AsSpan().SequenceEqual(fullB))
                        throw new InvalidOperationException("LaterDiscB.Create round-trip failed.");

                    if (LaterDiscUnion.Read(new byte[] { 0x83, 0x00, 0xF6, 0x07, 0x01 }, out int consumedA) is not LaterDiscA { Count: 7 } || consumedA != 4)
                        throw new InvalidOperationException("LaterDiscA streaming read failed.");
                    if (LaterDiscUnion.Read(new byte[] { 0x83, 0x00, 0xF6, 0x62, 0x68, 0x69, 0x01 }, out int consumedB) is not LaterDiscB { Label: "hi" } || consumedB != 6)
                        throw new InvalidOperationException("LaterDiscB streaming read failed.");

                    try { _ = new LaterDiscUnion(LaterDiscA.Read(nullB)); throw new InvalidOperationException("Wrong-case LaterDiscA ctor should fail."); }
                    catch (CborValidationException) { }
                    try { _ = new LaterDiscUnion(LaterDiscB.Read(nullA)); throw new InvalidOperationException("Wrong-case LaterDiscB ctor should fail."); }
                    catch (CborValidationException) { }
                    try { _ = LaterDiscUnion.Read(new byte[] { 0x82, 0x00, 0xF6 }); throw new InvalidOperationException("Truncated LaterDiscUnion.Read should fail."); }
                    catch (CborDecodeException) { }
                    try { _ = new LaterDiscUnion(LaterDiscA.Read(new byte[] { 0x82, 0x00, 0xF6 })); throw new InvalidOperationException("Truncated LaterDiscA ctor should fail."); }
                    catch (CborValidationException) { }
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("LaterDiscProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly.Load(image.ToArray()).GetType("LaterDiscProbe.Probe", true)!.GetMethod("Exercise")!.CreateDelegate<Action>()();
    }

    [Fact]
    public void OfficialUnionConstructorsValidateCaseReaderAcrossWritableRawShapes()
    {
        const string source = """
            #nullable enable
            global using System;
            using System.Buffers;
            using System.Runtime.CompilerServices;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            namespace WritableRawShapesProbe;

            [CborSerializable, CborList, CborIndex(0)]
            public partial record ListRecCase([CborOrder(0)] int Tag, [CborOrder(1)] int Count) : CborRecord;

            [CborSerializable, CborConstr(0)]
            public partial record ConstrRecCase([CborOrder(0)] int Value) : CborRecord;

            [CborSerializable]
            public partial record ContainerRecCase(string Text) : CborRecord;

            [CborSerializable]
            public partial record ContainerIntRecCase(int Value) : CborRecord;

            [CborSerializable, CborUnion(typeof(ListRecCase), typeof(ConstrRecCase), typeof(ContainerRecCase), typeof(ContainerIntRecCase)), Union]
            public readonly partial struct WritableRecUnion : ICborType;

            [CborSerializable]
            public partial record ValidatedRecCase(int Value) : CborRecord;

            public sealed class ValidatedRecCaseValidator : ICborValidator<ValidatedRecCase>
            {
                public bool Validate(ValidatedRecCase input) => input.Value >= 0;
            }

            [CborSerializable, CborUnion(typeof(ValidatedRecCase)), Union]
            public readonly partial struct ValidatedRecUnion : ICborType;

            public struct ManualRejectCase : ICborType
            {
                public ReadOnlyMemory<byte> Raw { get; set; }
                public int ConstrIndex => -1;
                public bool IsIndefinite => false;
                public static ManualRejectCase Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
                {
                    SAIB.Cbor.Serialization.CborReader r = new(data.Span);
                    r.ReadBeginArray();
                    if (r.ReadSize() != 1) { throw new CborDecodeException(0, nameof(ManualRejectCase), "1-item array", "wrong arity"); }
                    _ = r.ReadInt32();
                    bytesConsumed = data.Length - r.Buffer.Length;
                    return new() { Raw = data[..bytesConsumed] };
                }
                public static void Write(IBufferWriter<byte> output, ManualRejectCase value) =>
                    output.Write(value.Raw.Span);
            }

            [CborSerializable, CborUnion(typeof(ManualRejectCase)), Union]
            public readonly partial struct ManualRejectUnion : ICborType;

            public struct ManualUnderConsumeCase : ICborType
            {
                public ReadOnlyMemory<byte> Raw { get; set; }
                public int ConstrIndex => -1;
                public bool IsIndefinite => false;
                public static ManualUnderConsumeCase Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
                {
                    if (!data.IsEmpty && data.Span[0] == 0x82)
                    {
                        bytesConsumed = Math.Min(1, data.Length);
                        return new() { Raw = data[..bytesConsumed] };
                    }
                    SAIB.Cbor.Serialization.CborReader r = new(data.Span);
                    _ = r.ReadInt32();
                    bytesConsumed = data.Length - r.Buffer.Length;
                    return new() { Raw = data[..bytesConsumed] };
                }
                public static void Write(IBufferWriter<byte> output, ManualUnderConsumeCase value) =>
                    output.Write(value.Raw.Span);
            }

            [CborSerializable, CborUnion(typeof(ManualUnderConsumeCase)), Union]
            public readonly partial struct ManualUnderConsumeUnion : ICborType;

            public static class Probe
            {
                public static void Exercise()
                {
                    byte[] okList = [0x82, 0x00, 0x07];
                    byte[] badList = [0x82, 0x00, 0x61, 0x78];
                    ListRecCase validList = ListRecCase.Read(okList);
                    if (WritableRecUnion.Read(okList) is not ListRecCase { Tag: 0, Count: 7 }
                        || !CborSerializer.Serialize(new WritableRecUnion(validList)).AsSpan().SequenceEqual(okList))
                        throw new InvalidOperationException("ListRecCase valid round-trip failed.");
                    ExpectDecodeFail(() => _ = WritableRecUnion.Read(badList), "ListRecCase Read");
                    ExpectValidationFail(() => _ = new WritableRecUnion(validList with { Raw = badList }), "ListRecCase ctor");

                    byte[] okConstr = [0xD8, 0x79, 0x81, 0x07];
                    byte[] badConstr = [0xD8, 0x79, 0x81, 0x61, 0x78];
                    ConstrRecCase validConstr = ConstrRecCase.Read(okConstr);
                    if (WritableRecUnion.Read(okConstr) is not ConstrRecCase { Value: 7 }
                        || !CborSerializer.Serialize(new WritableRecUnion(validConstr)).AsSpan().SequenceEqual(okConstr))
                        throw new InvalidOperationException("ConstrRecCase valid round-trip failed.");
                    ExpectDecodeFail(() => _ = WritableRecUnion.Read(badConstr), "ConstrRecCase Read");
                    ExpectValidationFail(() => _ = new WritableRecUnion(validConstr with { Raw = badConstr }), "ConstrRecCase ctor");

                    byte[] okText = [0x62, 0x6F, 0x6B];
                    byte[] badChunkText = [0x7F, 0x41, 0x01, 0xFF];
                    ContainerRecCase validText = ContainerRecCase.Read(okText);
                    if (WritableRecUnion.Read(okText) is not ContainerRecCase { Text: "ok" }
                        || !CborSerializer.Serialize(new WritableRecUnion(validText)).AsSpan().SequenceEqual(okText))
                        throw new InvalidOperationException("ContainerRecCase valid round-trip failed.");
                    ExpectDecodeFail(() => _ = WritableRecUnion.Read(badChunkText), "ContainerRecCase Read");
                    ExpectValidationFail(() => _ = new WritableRecUnion(validText with { Raw = badChunkText }), "ContainerRecCase ctor");

                    byte[] okInt = [0x07];
                    byte[] badIntOverflow = [0x1A, 0xFF, 0xFF, 0xFF, 0xFF];
                    ContainerIntRecCase validInt = ContainerIntRecCase.Read(okInt);
                    if (WritableRecUnion.Read(okInt) is not ContainerIntRecCase { Value: 7 }
                        || !CborSerializer.Serialize(new WritableRecUnion(validInt)).AsSpan().SequenceEqual(okInt))
                        throw new InvalidOperationException("ContainerIntRecCase valid round-trip failed.");
                    ExpectDecodeFail(() => _ = WritableRecUnion.Read(badIntOverflow), "ContainerIntRecCase Read");
                    ExpectValidationFail(() => _ = new WritableRecUnion(validInt with { Raw = badIntOverflow }), "ContainerIntRecCase ctor");

                    byte[] okValidated = [0x05];
                    byte[] badValidated = [0x20];
                    ValidatedRecCase validRec = ValidatedRecCase.Read(okValidated);
                    if (ValidatedRecUnion.Read(okValidated) is not ValidatedRecCase { Value: 5 }
                        || !CborSerializer.Serialize(new ValidatedRecUnion(validRec)).AsSpan().SequenceEqual(okValidated))
                        throw new InvalidOperationException("ValidatedRecCase valid round-trip failed.");
                    ExpectDecodeFail(() => _ = ValidatedRecUnion.Read(badValidated), "ValidatedRecCase Read");
                    ExpectValidationFail(() => _ = new ValidatedRecUnion(validRec with { Raw = badValidated }), "ValidatedRecCase ctor");

                    byte[] okManual = [0x81, 0x07];
                    byte[] badManual = [0x81, 0x61, 0x78];
                    ManualRejectCase validManual = ManualRejectCase.Read(okManual, out _);
                    if (ManualRejectUnion.Read(okManual) is not ManualRejectCase
                        || !CborSerializer.Serialize(new ManualRejectUnion(validManual)).AsSpan().SequenceEqual(okManual))
                        throw new InvalidOperationException("ManualRejectCase valid round-trip failed.");
                    ExpectDecodeFail(() => _ = ManualRejectUnion.Read(badManual), "ManualRejectCase Read");
                    ExpectValidationFail(() => _ = new ManualRejectUnion(new ManualRejectCase { Raw = badManual }), "ManualRejectCase ctor");

                    byte[] okUnder = [0x07];
                    byte[] badUnder = [0x82, 0x01, 0x02];
                    ManualUnderConsumeCase validUnder = ManualUnderConsumeCase.Read(okUnder, out _);
                    if (ManualUnderConsumeUnion.Read(okUnder) is not ManualUnderConsumeCase
                        || !CborSerializer.Serialize(new ManualUnderConsumeUnion(validUnder)).AsSpan().SequenceEqual(okUnder))
                        throw new InvalidOperationException("ManualUnderConsumeCase valid round-trip failed.");
                    ExpectDecodeFail(() => _ = ManualUnderConsumeUnion.Read(badUnder), "ManualUnderConsumeCase Read");
                    ExpectValidationFail(() => _ = new ManualUnderConsumeUnion(new ManualUnderConsumeCase { Raw = badUnder }), "ManualUnderConsumeCase ctor");
                }

                private static void ExpectDecodeFail(Action act, string label)
                {
                    try { act(); throw new InvalidOperationException($"{label} should throw CborDecodeException."); }
                    catch (CborDecodeException) { }
                }

                private static void ExpectValidationFail(Action act, string label)
                {
                    try { act(); throw new InvalidOperationException($"{label} should throw CborValidationException."); }
                    catch (CborValidationException) { }
                }
            }
            """;
        CSharpCompilation input = CreateCompilation("WritableRawShapesProbe", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(input, out Compilation output, out _);
        Diagnostic[] errors = [.. driver.GetRunResult().Diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly.Load(image.ToArray()).GetType("WritableRawShapesProbe.Probe", true)!.GetMethod("Exercise")!.CreateDelegate<Action>()();
    }
}

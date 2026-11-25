using Chrysalis.Codec.Serialization;
using System.Globalization;
using System.Reflection;
using System.Text;
using Chrysalis.Codec.CodeGen;
using Chrysalis.Codec.Serialization.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    private const int Iterations = 64;
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);
    private static readonly MetadataReference[] References = CreateReferences();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ConcurrentCompilations_KeepUnionProbeRegistryIsolated(int run)
    {
        string suffix = run.ToString(CultureInfo.InvariantCulture);
        CSharpCompilation left = CreateCompilation("GeneratorIsolationLeft" + suffix);
        CSharpCompilation right = CreateCompilation("GeneratorIsolationRight" + suffix);

        // Warm both compilations serially and establish the exact expected reader for each.
        string expectedLeft = GenerateUnionReader(left);
        string expectedRight = GenerateUnionReader(right);
        Assert.Contains("_ResolveIdx2", expectedLeft, StringComparison.Ordinal);
        Assert.Contains("_ResolveIdx2", expectedRight, StringComparison.Ordinal);

        // Dedicated workers and a per-iteration rendezvous exercise two independent compiler
        // invocations sharing one generator assembly, just as the compiler server does.
        using Barrier rendezvous = new(2);
        Task<string[]> leftTask = Task.Factory.StartNew(
            () => GenerateRepeatedly(left, rendezvous), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<string[]> rightTask = Task.Factory.StartNew(
            () => GenerateRepeatedly(right, rendezvous), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        string[][] actual = await Task.WhenAll(leftTask, rightTask);

        Assert.All(actual[0], source => Assert.Equal(expectedLeft, source));
        Assert.All(actual[1], source => Assert.Equal(expectedRight, source));
    }

    [Fact]
    public void ConsumerLedgerTextLookalikeDoesNotChangeReader()
    {
        const string source = """
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            using Chrysalis.Codec.Serialization;
            namespace Chrysalis.Codec.Serialization.Attributes;
            internal sealed class CborLedgerTextAttribute : Attribute;
            [CborSerializable, CborList]
            public readonly partial record struct Probe : ICborType
            {
                [CborOrder(0), CborLedgerText] public partial string Value { get; }
            }
            """;
        CSharpCompilation compilation = CreateCompilation("ConsumerLedgerText", source);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain("LedgerText.Skip", result.Results.Single().GeneratedSources.Single(
            generated => generated.HintName.EndsWith(".Probe.Serializer.g.cs", StringComparison.Ordinal)).SourceText.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("CborList")]
    [InlineData("CborConstr(0)")]
    public void LazyListReaderValidatesCapturedValue(string encoding)
    {
        string source = $$"""
            global using System;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace ValidatorProbe;
            [CborSerializable, {{encoding}}]
            public readonly partial record struct Checked : ICborType
            {
                [CborOrder(0)] public partial int Value { get; }
            }
            public readonly struct Validator : ICborValidator<Checked>
            {
                public bool Validate(Checked value) => value.Value >= 0;
            }
            """;
        CSharpCompilation compilation = CreateCompilation("ValidatorProbe", source);
        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        Assert.Empty(result.Diagnostics);
        string generated = result.Results.Single().GeneratedSources.Single(source =>
            source.HintName == "ValidatorProbe.Checked.Serializer.g.cs").SourceText.ToString();
        Assert.Contains("if (!validator.Validate(result))", generated, StringComparison.Ordinal);
        Assert.Contains("return result;", generated, StringComparison.Ordinal);
        using MemoryStream assemblyBytes = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(assemblyBytes);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly assembly = Assembly.Load(assemblyBytes.ToArray());
        Type type = assembly.GetType("ValidatorProbe.Checked", throwOnError: true)!;
        MethodInfo read = type.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
        string prefix = encoding == "CborList" ? "" : "d879";
        _ = read.Invoke(null, [new ReadOnlyMemory<byte>(Convert.FromHexString(prefix + "8100"))]);
        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
            read.Invoke(null, [new ReadOnlyMemory<byte>(Convert.FromHexString(prefix + "8120"))]));
        Assert.Equal("validation failed", Assert.IsType<CborValidationException>(exception.InnerException).Actual);
    }

    private static string[] GenerateRepeatedly(CSharpCompilation compilation, Barrier rendezvous)
    {
        string[] result = new string[Iterations];
        for (int i = 0; i < result.Length; i++)
        {
            if (!rendezvous.SignalAndWait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("The other generator worker did not reach the rendezvous.");
            }
            result[i] = GenerateUnionReader(compilation);
        }
        return result;
    }

    [Theory]
    [InlineData("Chrysalis.Network")]
    [InlineData("Chrysalis.Codec")]
    public void NetworkAssemblyAndUnionNamesDoNotEnableTrials(string assemblyName)
    {
        const string source = """
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace Chrysalis.Network.Cbor.Handshake
            {
                [CborSerializable, CborUnion] public partial interface HandshakeMessage : ICborType;
                [CborSerializable, CborUnion] public partial interface VersionTable : ICborType;
                [CborSerializable, CborUnion] public partial interface Fake : ICborType;
            }
            namespace Chrysalis.Network.Cbor.ChainSync
            {
                [CborSerializable, CborUnion] public partial interface INetworkBlockHeader : ICborType;
            }
            namespace Chrysalis.Network.Cbor.LocalStateQuery
            {
                [CborSerializable, CborUnion] public partial interface IQueryReq : ICborType;
            }
            """;
        GeneratorDriverRunResult result = CreateDriver().RunGenerators(CreateCompilation(assemblyName, source)).GetRunResult();
        Assert.Equal(5, result.Diagnostics.Count(diagnostic => diagnostic.Id == "CHCB001" && diagnostic.Location.IsInSource));
        Assert.DoesNotContain(result.Results.Single().GeneratedSources, file =>
            file.SourceText.ToString().Contains("BeginReadTrial", StringComparison.Ordinal));
    }

    [Fact]
    public void DeclaredUnionReaderInAnotherPartialKeepsConvenienceReadAndWriter()
    {
        const string source = """
            global using System;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace ReaderProbe;
            [CborSerializable, CborUnion] public partial interface IValue : ICborType;
            [CborSerializable, CborList] public partial record First([CborOrder(0)] int Value) : Chrysalis.Codec.Types.CborRecord, IValue;
            [CborSerializable, CborList] public partial record Second([CborOrder(0)] int Value) : Chrysalis.Codec.Types.CborRecord, IValue;
            """;
        const string partial = """
            namespace ReaderProbe;
            using Bytes = global::System.ReadOnlyMemory<byte>;
            public partial interface IValue
            {
                static IValue Read(Bytes input, out int count) => Second.Read(input, out count);
            }
            """;
        CSharpCompilation compilation = CreateCompilation("ReaderProbe", source)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(partial, ParseOptions, "Read.cs"));
        GeneratorDriverRunResult result = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _).GetRunResult();
        Assert.Empty(result.Diagnostics);
        using MemoryStream assemblyBytes = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = output.Emit(assemblyBytes);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Type type = Assembly.Load(assemblyBytes.ToArray()).GetType("ReaderProbe.IValue", throwOnError: true)!;
        MethodInfo read = type.GetMethod("Read", [typeof(ReadOnlyMemory<byte>)])!;
        Assert.Equal("ReaderProbe.Second", read.Invoke(null, [new ReadOnlyMemory<byte>([0x81, 0x01])])?.GetType().FullName);
        Assert.NotNull(type.GetMethod("Write", BindingFlags.Public | BindingFlags.Static));
    }

    [Theory]
    [InlineData("public static IValue Read(ReadOnlyMemory<byte> data, Era era) => First.Read(data);")]
    [InlineData("public static IValue Read(ReadOnlyMemory<byte> data, int era) => First.Read(data);")]
    [InlineData("public static IValue Read(string hex) => First.Read(Convert.FromHexString(hex));")]
    [InlineData("public void Read(int value) { }")]
    public void UnrelatedReadOverloadDoesNotSuppressGeneratedReader(string method)
    {
        string source = $$"""
            global using System;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace ReaderProbe;
            public enum Era { One }
            [CborSerializable, CborUnion] public partial interface IValue : ICborType
            {
                {{method}}
            }
            [CborSerializable, CborList] public partial record First([CborOrder(0)] int Value) : Chrysalis.Codec.Types.CborRecord, IValue;
            """;
        GeneratorDriverRunResult result = CreateDriver()
            .RunGeneratorsAndUpdateCompilation(CreateCompilation("ContextualReaderProbe", source), out Compilation output, out _).GetRunResult();
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("public static object Read(ReadOnlyMemory<byte> data, out int count) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(ReadOnlySpan<byte> data, out int count) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(ReadOnlyMemory<byte> data, ref int count) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(ReadOnlyMemory<byte> data, out long count) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(ReadOnlyMemory<byte> data, long count) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(ReadOnlyMemory<byte> data, uint count) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(out int count, ReadOnlyMemory<byte> data) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(in ReadOnlyMemory<byte> data, Era era) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(ref ReadOnlyMemory<byte> data, out int count) => throw new NotImplementedException();")]
    [InlineData("public static IValue? Read(ReadOnlyMemory<byte> data, out int count) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(ReadOnlyMemory<byte> data) => throw new NotImplementedException(); public static IValue Read(ReadOnlyMemory<byte> data, out int count) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read(ReadOnlyMemory<byte> data, out int count) => throw new NotImplementedException(); public static IValue Read(ReadOnlyMemory<byte> data) => throw new NotImplementedException();")]
    [InlineData("public IValue Read(ReadOnlyMemory<byte> data, out int count) => throw new NotImplementedException();")]
    [InlineData("private static IValue Read(ReadOnlyMemory<byte> data, out int count) => throw new NotImplementedException();")]
    [InlineData("public static IValue Read<T>(ReadOnlyMemory<byte> data, out int count) => throw new NotImplementedException();")]
    [InlineData("public static ref IValue Read(ReadOnlyMemory<byte> data, out int count) => throw new NotImplementedException();")]
    [InlineData("public static abstract IValue Read(ReadOnlyMemory<byte> data, out int count);")]
    [InlineData("public static IValue Read(ReadOnlyMemory<byte> data) => throw new NotImplementedException();")]
    public void UnionReaderNearMissHasASourceDiagnostic(string method)
    {
        string source = $$"""
            #nullable enable
            global using System;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace ReaderProbe;
            public enum Era { One }
            [CborSerializable, CborUnion] public partial interface IValue : ICborType { {{method}} }
            """;
        GeneratorDriverRunResult result = CreateDriver()
            .RunGeneratorsAndUpdateCompilation(CreateCompilation("ReaderProbe", source), out Compilation output, out _).GetRunResult();
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "CHCB003");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.DoesNotContain(output.GetDiagnostics(), reported => reported.Id == "CS0111");
        Assert.Contains("Read(ReadOnlyMemory<byte>, out int)", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Some<byte[]>")]
    [InlineData("CborList<ReadOnlyMemory<byte>>")]
    public void X46PlutusDataBytesInAGenericTypeHaveAWarning(string field)
    {
        string source = $$"""
            global using System;
            using Chrysalis.Codec.Serialization.Attributes;
            using Chrysalis.Codec.Types;
            using Chrysalis.Codec.Types.Plutus;
            namespace BytesProbe;
            [PlutusData(0)] public partial record Datum([CborOrder(0)] {{field}} Owner) : CborRecord;
            """;
        GeneratorDriverRunResult result = CreateDriver()
            .RunGeneratorsAndUpdateCompilation(CreateCompilation("BytesProbe", source), out _, out _).GetRunResult();
        Diagnostic diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "CHCB004");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Contains("'Owner'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CborList", "int", false)]
    [InlineData("CborList", "int?", false)]
    [InlineData("CborList, CborIndex(0)", "int", false)]
    [InlineData("CborList", "int", true)]
    public void X7ReportsAmbiguityAndKeepsGeneratedOrderAcrossSyntaxTreeOrders(string shape, string type, bool trailingOptional)
    {
        const string imports = "using Chrysalis.Codec.Serialization; using Chrysalis.Codec.Serialization.Attributes; namespace Snapshot; ";
        SyntaxTree root = CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.Linq; " + imports + "[CborSerializable, CborUnion] public partial interface IAmbiguous : ICborType;", ParseOptions, "Root.cs");
        SyntaxTree first = CSharpSyntaxTree.ParseText(imports + "[CborSerializable, CborList] public readonly partial record struct A : IAmbiguous { [CborOrder(0)] public partial int Value { get; } }", ParseOptions, "A.cs");
        string tail = trailingOptional ? "[CborOrder(1)] public partial int? Tail { get; }" : "";
        SyntaxTree second = CSharpSyntaxTree.ParseText(imports + $$"""[CborSerializable, {{shape}}] public readonly partial record struct B : IAmbiguous { [CborOrder(0)] public partial {{type}} Value { get; } {{tail}} }""", ParseOptions, "B.cs");
        static string Generate(SyntaxTree[] trees)
        {
            CSharpCompilation compilation = CSharpCompilation.Create("Ambiguous", trees, References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriverRunResult result = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation generatedCompilation, out _).GetRunResult();
            Assert.DoesNotContain(generatedCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Diagnostic report = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "CHCB002");
            Assert.Equal("Root.cs", report.Location.SourceTree?.FilePath);
            return result.Results.Single().GeneratedSources.Single(x => x.HintName == "Snapshot.IAmbiguous.Serializer.g.cs").SourceText.ToString();
        }

        string generated = Generate([root, first, second]);
        Assert.Equal(generated, Generate([second, root, first]));
        Assert.DoesNotContain("BeginReadTrial", generated, StringComparison.Ordinal);
    }

    private static string GenerateUnionReader(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CreateDriver();
        GeneratorDriverRunResult result = driver.RunGenerators(compilation).GetRunResult();
        if (!result.Diagnostics.IsEmpty)
        {
            return string.Join(Environment.NewLine, result.Diagnostics);
        }
        string hintName = compilation.AssemblyName + ".ProbeResponse.Serializer.g.cs";
        return result.Results.Single().GeneratedSources.Single(source => source.HintName == hintName).SourceText.ToString();
    }

    private static CSharpCompilation CreateCompilation(string name)
    {
        StringBuilder padding = new();
        for (int i = 0; i < 8; i++)
        {
            _ = padding.Append("[CborSerializable, CborList] public partial record Padding")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .AppendLine("([CborOrder(0)] int Value);");
        }

        // The two variants have the same index, but bytes versus a generated list payload.
        // Padding widens the emission window after a registry is published without sleeps.
        string source = $$"""
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace {{name}};

            [CborSerializable, CborList]
            public partial record HeaderPayload([CborOrder(0)] int Value) : ICborType
            {
                public ReadOnlyMemory<byte> Raw { get; set; }
                public int ConstrIndex { get; set; }
                public bool IsIndefinite { get; set; }
            }

            {{padding}}

            [CborSerializable, CborUnion]
            public abstract partial record ProbeResponse : ICborType
            {
                public ReadOnlyMemory<byte> Raw { get; set; }
                public int ConstrIndex { get; set; }
                public bool IsIndefinite { get; set; }
            }

            [CborSerializable, CborList, CborIndex(2)]
            public partial record ForwardBytes(
                [CborOrder(0)] int Idx, [CborOrder(1)] ReadOnlyMemory<byte> Payload) : ProbeResponse;

            [CborSerializable, CborList, CborIndex(2)]
            public partial record ForwardArray(
                [CborOrder(0)] int Idx, [CborOrder(1)] HeaderPayload Payload) : ProbeResponse;
            """;
        return CreateCompilation(name, source);
    }

    private static CSharpCompilation CreateCompilation(string name, string source) =>
        CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source, ParseOptions)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static CSharpGeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
        [new CborSerializerCodeGen().AsSourceGenerator()], parseOptions: ParseOptions);

    private static MetadataReference[] CreateReferences()
    {
        string platformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The test host did not provide its platform assemblies.");
        return [.. platformAssemblies.Split(Path.PathSeparator)
            .Append(typeof(CborSerializableAttribute).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    }
}

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
        Assert.Equal("Validation failed", Assert.IsType<Exception>(exception.InnerException).Message);
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

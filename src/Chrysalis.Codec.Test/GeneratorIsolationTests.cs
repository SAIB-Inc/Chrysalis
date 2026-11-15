using System.Globalization;
using System.Text;
using Chrysalis.Codec.CodeGen;
using Chrysalis.Codec.Serialization.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Chrysalis.Codec.Test;

public class GeneratorIsolationTests
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
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CborSerializerCodeGen().AsSourceGenerator()], parseOptions: ParseOptions);
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
        return CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source, ParseOptions)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

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

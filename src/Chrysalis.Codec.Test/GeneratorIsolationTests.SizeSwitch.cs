using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.Test;

public partial class GeneratorIsolationTests
{
    [Fact]
    public void DisjointHeaderBodyRangesSwitchOnTheFullLengthAheadOfTheWalk()
    {
        string declarations = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "HeaderBody.cs.txt"))
            .Replace("namespace Chrysalis.Codec.Types.Cardano.Core.Header;", "namespace HeaderBodyProbe;", StringComparison.Ordinal);
        Assert.Contains("namespace HeaderBodyProbe;", declarations, StringComparison.Ordinal);
        GeneratorDriver driver = GenerateWithoutErrors("HeaderBodyProbe", "global using System;\nusing Chrysalis.Codec.Types.Cardano.Core.Header;\n" + declarations);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        string reader = GeneratedText(driver, "HeaderBodyProbe.IBlockHeaderBody");
        int start = reader.IndexOf("try {", StringComparison.Ordinal);
        int size = reader.IndexOf("switch (_sizeReader.ReadSize())", StringComparison.Ordinal);
        Assert.True(start >= 0 && start < size && size < reader.IndexOf("_Matches0(new CborReader", StringComparison.Ordinal), reader);
        Assert.Contains("case 15: return HeaderBodyProbe.AlonzoHeaderBody.Read(data, out bytesConsumed);", reader, StringComparison.Ordinal);
        Assert.Contains("case 10: return HeaderBodyProbe.BabbageHeaderBody.Read(data, out bytesConsumed);", reader, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingSizeRangesKeepOnlyTheShapeWalk(bool disjointThirdCase)
    {
        string third = disjointThirdCase
            ? "[CborSerializable, CborList] public readonly partial record struct Wide : IRange { [CborOrder(0)] public partial int A { get; } [CborOrder(1)] public partial int B { get; } [CborOrder(2)] public partial int C { get; } }"
            : "";
        string source = $$"""
            global using System;
            using Chrysalis.Codec.Serialization;
            using Chrysalis.Codec.Serialization.Attributes;
            namespace RangeProbe;
            [CborSerializable, CborUnion] public partial interface IRange : ICborType;
            [CborSerializable, CborList] public readonly partial record struct Short : IRange { [CborOrder(0)] public partial int Value { get; } }
            [CborSerializable, CborList] public readonly partial record struct Long : IRange { [CborOrder(0)] public partial int Value { get; } [CborOrder(1)] public partial int? Tail { get; } }
            {{third}}
            """;
        GeneratorDriver driver = GenerateWithoutErrors("RangeProbe", source);
        _ = Assert.Single(driver.GetRunResult().Diagnostics, diagnostic => diagnostic.Id == "CHCB002");
        string reader = GeneratedText(driver, "RangeProbe.IRange");
        Assert.Contains("_Matches0(new CborReader", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("_sizeReader", reader, StringComparison.Ordinal);
    }

    private static GeneratorDriver GenerateWithoutErrors(string name, string source)
    {
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(CreateCompilation(name, source), out Compilation output, out _);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return driver;
    }
}

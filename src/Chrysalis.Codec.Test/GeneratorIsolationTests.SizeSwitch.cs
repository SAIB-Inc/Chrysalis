using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        StatementSyntax[] body = UnwrappedReadBody(GeneratedText(driver, "HeaderBodyProbe.IBlockHeaderBody"));
        IfStatementSyntax guard = Assert.IsType<IfStatementSyntax>(body[0]);
        Assert.Contains(guard.Condition.DescendantNodes().OfType<BinaryExpressionSyntax>(),
            binary => binary.IsKind(SyntaxKind.EqualsExpression) && binary.Right is LiteralExpressionSyntax { Token.Value: 4 });
        SwitchStatementSyntax sizeSwitch = Assert.Single(guard.Statement.DescendantNodes().OfType<SwitchStatementSyntax>());
        Assert.Equal("ReadSize", Assert.IsType<MemberAccessExpressionSyntax>(
            Assert.IsType<InvocationExpressionSyntax>(sizeSwitch.Expression).Expression).Name.Identifier.Text);
        AssertCaseReturn(sizeSwitch, 15, "HeaderBodyProbe.AlonzoHeaderBody.Read(data, out bytesConsumed)");
        AssertCaseReturn(sizeSwitch, 10, "HeaderBodyProbe.BabbageHeaderBody.Read(data, out bytesConsumed)");
        Assert.DoesNotContain(guard.DescendantNodes().OfType<InvocationExpressionSyntax>(), IsMatches0);
        Assert.Contains(body.Skip(1).SelectMany(statement => statement.DescendantNodes().OfType<InvocationExpressionSyntax>()), IsMatches0);
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
        StatementSyntax[] body = UnwrappedReadBody(GeneratedText(driver, "RangeProbe.IRange"));
        Assert.True(Assert.IsType<IfStatementSyntax>(body[0]).Condition is InvocationExpressionSyntax call && IsMatches0(call));
    }

    private static StatementSyntax[] UnwrappedReadBody(string source)
    {
        SyntaxList<StatementSyntax> statements = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "Read" && method.ParameterList.Parameters.Count == 2).Body!.Statements;
        int start = statements.TakeWhile(statement => statement is LocalDeclarationStatementSyntax).Count();
        while (start < statements.Count && statements[start] is TryStatementSyntax outer)
        {
            statements = outer.Block.Statements;
            start = 0;
        }
        return [.. statements.Skip(start)];
    }

    private static bool IsMatches0(InvocationExpressionSyntax call) => call.Expression is IdentifierNameSyntax { Identifier.Text: "_Matches0" };

    private static void AssertCaseReturn(SwitchStatementSyntax sizeSwitch, int size, string invocation)
    {
        SwitchSectionSyntax section = Assert.Single(sizeSwitch.Sections, s => s.Labels.OfType<CaseSwitchLabelSyntax>()
            .Any(label => label.Value is LiteralExpressionSyntax { Token.Value: int value } && value == size));
        Assert.Equal(invocation, Assert.IsType<ReturnStatementSyntax>(Assert.Single(section.Statements)).Expression?.NormalizeWhitespace().ToFullString());
    }

    private static GeneratorDriver GenerateWithoutErrors(string name, string source)
    {
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(CreateCompilation(name, source), out Compilation output, out _);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return driver;
    }
}

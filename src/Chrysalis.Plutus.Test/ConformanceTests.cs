using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Text;
using Chrysalis.Plutus.Types;
using Xunit;

namespace Chrysalis.Plutus.Test;

/// <summary>
/// Runs the Plutus Core conformance test suite.
/// Each test: parse UPLC text → DeBruijn convert → CEK evaluate → compare output + budget.
/// Ported from blaze-plutus conformance.test.ts.
/// </summary>
public class ConformanceTests
{
    private const long I64Max = long.MaxValue;

    private static readonly string ConformanceDir = Path.Combine(
        AppContext.BaseDirectory, "conformance");

    public static IEnumerable<object[]> DiscoverTests()
    {
        if (!Directory.Exists(ConformanceDir))
        {
            yield break;
        }

        foreach (string uplcPath in Directory.EnumerateFiles(ConformanceDir, "*.uplc", SearchOption.AllDirectories))
        {
            if (uplcPath.EndsWith(".expected", StringComparison.Ordinal))
            {
                continue;
            }

            string expectedPath = uplcPath + ".expected";
            string budgetPath = uplcPath + ".budget.expected";

            if (!File.Exists(expectedPath))
            {
                continue;
            }

            // Test name: relative path from conformance dir to the leaf folder
            string testName = Path.GetRelativePath(ConformanceDir, Path.GetDirectoryName(uplcPath)!);
            yield return [testName, uplcPath, expectedPath, budgetPath];
        }
    }

    [Theory]
    [MemberData(nameof(DiscoverTests))]
    public void Conformance(string testName, string uplcPath, string expectedPath, string budgetPath)
    {
        _ = testName; // used for display only

        string source = File.ReadAllText(uplcPath);
        string expected = File.ReadAllText(expectedPath);
        string? budgetExpected = File.Exists(budgetPath) ? File.ReadAllText(budgetPath).Trim() : null;

        bool expectParseError = expected.StartsWith("parse error", StringComparison.Ordinal);
        bool expectEvalFailure = expected.StartsWith("evaluation failure", StringComparison.Ordinal);

        // Step 1: Parse
        Program<Name> program;
        try
        {
            program = UplcParser.Parse(source);
        }
        catch (ParseException)
        {
            if (expectParseError)
            {
                return; // PASS
            }

            throw;
        }

        if (expectParseError)
        {
            Assert.Fail($"Expected parse error but parsing succeeded: {uplcPath}");
        }

        // Step 2: Convert name → DeBruijn
        Program<DeBruijn> dProgram;
        try
        {
            dProgram = UplcParser.NameToDeBruijn(program);
        }
        catch (ConvertException)
        {
            if (expectEvalFailure)
            {
                return; // PASS — conversion failure counts as eval failure
            }

            throw;
        }

        // Step 3: Evaluate
        ExBudget initialBudget = ExBudget.Unlimited;
        CekMachine machine = new(initialBudget);
        Term<DeBruijn> result;
        try
        {
            result = machine.Run(dProgram.Term);
        }
        catch (EvaluationException)
        {
            if (expectEvalFailure)
            {
                return; // PASS
            }

            throw;
        }

        if (expectEvalFailure)
        {
            Assert.Fail($"Expected evaluation failure but succeeded: {uplcPath}");
        }

        // Step 4: Parse and convert expected output
        Program<Name> expectedProgram = UplcParser.Parse(expected);
        Program<DeBruijn> dExpected = UplcParser.NameToDeBruijn(expectedProgram);

        // Step 5: Compare via pretty printing
        string prettyResult = UplcParser.PrettyPrint(result);
        string prettyExpectedTerm = UplcParser.PrettyPrint(dExpected.Term);

        Assert.Equal(prettyExpectedTerm, prettyResult);

        // Step 6: Compare budget
        if (budgetExpected is not null && !budgetExpected.StartsWith("evaluation failure", StringComparison.Ordinal))
        {
            ExBudget remaining = machine.RemainingBudget;
            // When using unlimited budget, remaining can go negative (long overflow).
            // Cap consumed at I64Max in that case.
            long consumedCpu = remaining.Cpu < 0 ? I64Max : Math.Min(I64Max, initialBudget.Cpu - remaining.Cpu);
            long consumedMem = remaining.Mem < 0 ? I64Max : Math.Min(I64Max, initialBudget.Mem - remaining.Mem);
            string actualBudget = $"({{cpu: {consumedCpu}\n| mem: {consumedMem}}})";

            Assert.Equal(budgetExpected, actualBudget);
        }
    }
}

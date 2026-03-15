using Chrysalis.Tx.Builders;
using Xunit;

namespace Chrysalis.Tx.Test;

public class TransactionValidatorTests
{
    private static TransactionBuilder CreateBuilder() => new();

    [Fact]
    public void DetectsDuplicateInputs()
    {
        TransactionBuilder builder = CreateBuilder();
        builder.AddInput("AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD", 0);
        builder.AddInput("AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD", 0);

        List<string> issues = TransactionValidator.Validate(builder);
        Assert.Contains(issues, i => i.Contains("Duplicate input", StringComparison.Ordinal));
    }

    [Fact]
    public void NoDuplicatesWhenInputsDiffer()
    {
        TransactionBuilder builder = CreateBuilder();
        builder.AddInput("AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD", 0);
        builder.AddInput("AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD", 1);

        List<string> issues = TransactionValidator.Validate(builder);
        Assert.DoesNotContain(issues, i => i.Contains("Duplicate input", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectsFeeNotSet()
    {
        TransactionBuilder builder = CreateBuilder();
        List<string> issues = TransactionValidator.Validate(builder);
        Assert.Contains(issues, i => i.Contains("Fee is not set", StringComparison.Ordinal));
    }

    [Fact]
    public void NoFeeIssueWhenFeeIsSet()
    {
        TransactionBuilder builder = CreateBuilder();
        builder.SetFee(200_000);
        List<string> issues = TransactionValidator.Validate(builder);
        Assert.DoesNotContain(issues, i => i.Contains("Fee is not set", StringComparison.Ordinal));
    }
}

namespace Chrysalis.Plutus.Cek;

/// <summary>A script spent more CPU or memory than the caller allowed.</summary>
public sealed class BudgetExceededException : EvaluationException
{
    public BudgetExceededException() : base("The script exceeded its execution budget.") { }
    public BudgetExceededException(string message) : base(message) { }
    public BudgetExceededException(string message, Exception innerException) : base(message, innerException) { }
}

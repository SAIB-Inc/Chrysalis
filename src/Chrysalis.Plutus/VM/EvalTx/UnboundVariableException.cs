using Chrysalis.Plutus.Cek;

namespace Chrysalis.Plutus.VM.EvalTx;

/// <summary>The script refers to a variable no enclosing lambda binds, outside <c>constr</c> and <c>case</c>.</summary>
public sealed class UnboundVariableException : EvaluationException
{
    public UnboundVariableException() : base("The script has an unbound variable.") { }
    public UnboundVariableException(string message) : base(message) { }
    public UnboundVariableException(string message, Exception innerException) : base(message, innerException) { }
}

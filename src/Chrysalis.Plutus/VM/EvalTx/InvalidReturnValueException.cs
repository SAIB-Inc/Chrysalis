using Chrysalis.Plutus.Cek;

namespace Chrysalis.Plutus.VM.EvalTx;

/// <summary>A script other than Plutus V1 or V2 evaluated successfully but did not return built-in unit.</summary>
public sealed class InvalidReturnValueException : EvaluationException
{
    public InvalidReturnValueException() : base("Plutus scripts other than V1 and V2 must return built-in unit.") { }
    public InvalidReturnValueException(string message) : base(message) { }
    public InvalidReturnValueException(string message, Exception innerException) : base(message, innerException) { }
}

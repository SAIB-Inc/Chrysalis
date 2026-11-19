// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
namespace Chrysalis.Plutus.Cek;

public class EvaluationException : Exception
{
    public EvaluationException() { }
    public EvaluationException(string message) : base(message) { }
    public EvaluationException(string message, Exception innerException) : base(message, innerException) { }
}

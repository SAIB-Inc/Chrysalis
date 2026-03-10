namespace Chrysalis.Plutus.Text;

/// <summary>
/// Thrown when parsing UPLC text format fails.
/// </summary>
public sealed class ParseException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ParseException"/> class.</summary>
    public ParseException() : base() { }

    /// <summary>Initializes a new instance of the <see cref="ParseException"/> class with a message.</summary>
    public ParseException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="ParseException"/> class with a message and inner exception.</summary>
    public ParseException(string message, Exception innerException) : base(message, innerException) { }
}

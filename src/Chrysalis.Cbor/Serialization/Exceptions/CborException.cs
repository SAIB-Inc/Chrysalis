namespace Chrysalis.Cbor.Serialization.Exceptions;

/// <summary>
/// Base exception for all CBOR serialization/deserialization errors
/// </summary>
public class CborException : Exception
{
    public CborException(string message) : base(message) { }
    public CborException(string message, Exception inner) : base(message, inner) { }
}
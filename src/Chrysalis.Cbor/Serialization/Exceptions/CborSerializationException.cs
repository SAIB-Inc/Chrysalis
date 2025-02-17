namespace Chrysalis.Cbor.Serialization.Exceptions;

/// <summary>
/// Thrown when attempting to serialize invalid data
/// </summary>
public class CborSerializationException : CborException
{
    public CborSerializationException(string message) : base(message) { }
    public CborSerializationException(string message, Exception inner) : base(message, inner) { }
}
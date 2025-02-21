namespace Chrysalis.Cbor.Serialization.Exceptions;

/// <summary>
/// Thrown when attempting to deserialize invalid CBOR data
/// </summary>
public class CborDeserializationException : CborException
{
    public CborDeserializationException(string message) : base(message) { }
    public CborDeserializationException(string message, Exception inner) : base(message, inner) { }
}
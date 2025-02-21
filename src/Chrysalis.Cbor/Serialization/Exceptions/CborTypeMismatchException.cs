namespace Chrysalis.Cbor.Serialization.Exceptions;

/// <summary>
/// Thrown when encountering type mismatches during serialization/deserialization
/// </summary>
public class CborTypeMismatchException(string message, Type? expected = null, Type? actual = null) : CborException(message)
{
    public Type? ExpectedType { get; } = expected;
    public Type? ActualType { get; } = actual;
}
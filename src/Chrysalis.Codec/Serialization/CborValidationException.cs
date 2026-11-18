namespace Chrysalis.Codec.Serialization;

/// <summary>A CBOR value rejected by a codec validator or serialization constraint.</summary>
public class CborValidationException : CborDecodeException
{
    /// <summary>Creates a validation error without wire context.</summary>
    public CborValidationException() { }

    /// <summary>Creates a validation error with a custom message.</summary>
    public CborValidationException(string message) : base(message) { }

    /// <summary>Creates a validation error with a custom message and cause.</summary>
    public CborValidationException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates a validation error with structured context; writes have offset -1.</summary>
    public CborValidationException(int offset, string typePath, string expected, string actual, Exception? innerException = null)
        : base(offset, typePath, expected, actual, innerException) { }
}

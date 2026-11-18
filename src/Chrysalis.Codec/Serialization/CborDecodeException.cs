using System.Globalization;

namespace Chrysalis.Codec.Serialization;

/// <summary>A CBOR decoding failure with structured wire and type context.</summary>
public class CborDecodeException : FormatException
{
    private readonly object? _expected;
    private readonly string? _actual = "unknown";
    // Tag mismatches always start at zero. A missing actual string lets the offset slot carry the raw tag.
    private readonly ulong _positionOrTag;

    /// <summary>Creates a decoding error without wire context.</summary>
    public CborDecodeException() { }

    /// <summary>Creates a decoding error with a custom message.</summary>
    public CborDecodeException(string message) : base(message) { }

    /// <summary>Creates a decoding error with a custom message and cause.</summary>
    public CborDecodeException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates a decoding error at a byte offset relative to the input being decoded.</summary>
    public CborDecodeException(int offset, string typePath, string expected, string actual, Exception? innerException = null)
        : this(typePath, expected, actual, unchecked((ulong)offset), innerException) { }

    internal CborDecodeException(int offset, string typePath, Type expected, string actual)
        : this(typePath, expected, actual, unchecked((ulong)offset)) { }

    internal CborDecodeException(string typePath, string expected, ulong actualTag)
        : this(typePath, expected, null, actualTag) { }

    private CborDecodeException(string typePath, object expected, string? actual, ulong positionOrTag, Exception? innerException = null)
        : base(typePath, innerException)
    {
        // Keep the path in the base message slot; the override renders the structured message only on demand.
        _expected = expected;
        _actual = actual;
        _positionOrTag = positionOrTag;
    }

    /// <inheritdoc />
    public override string Message => _expected is null ? base.Message : string.Create(CultureInfo.InvariantCulture,
        $"CBOR {TypePath} at byte {Offset}: expected {Expected}; actual {Actual}.");

    /// <summary>The zero-based input byte offset, or -1 when no wire position is available.</summary>
    public int Offset => _expected is null ? -1 : _actual is null ? 0 : unchecked((int)_positionOrTag);

    /// <summary>The decoded type and, when available, its failing property.</summary>
    public string TypePath => _expected is null ? "unknown" : base.Message;

    /// <summary>The expected CBOR kind, shape, or validation condition.</summary>
    public string Expected => _expected is Type type ? CborError.Expected(type) : (string?)_expected ?? "valid CBOR";

    /// <summary>The observed CBOR kind or failure condition.</summary>
    public string Actual => _actual ?? _positionOrTag.ToString(CultureInfo.InvariantCulture);
}

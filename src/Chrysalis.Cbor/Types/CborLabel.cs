using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// COSE Label type that can be either an integer or a text string.
/// Based on RFC 8152: label = int / tstr.
/// </summary>
/// <param name="Value">The label value, which must be an int, long, or string.</param>
[CborSerializable]
public partial record CborLabel(object Value) : CborBase
{
    /// <summary>
    /// Initializes a new instance of <see cref="CborLabel"/> with an integer value.
    /// </summary>
    /// <param name="value">The integer label value.</param>
    public CborLabel(int value) : this((object)value) { }

    /// <summary>
    /// Initializes a new instance of <see cref="CborLabel"/> with a long integer value.
    /// </summary>
    /// <param name="value">The long integer label value.</param>
    public CborLabel(long value) : this((object)value) { }

    /// <summary>
    /// Initializes a new instance of <see cref="CborLabel"/> with a string value.
    /// </summary>
    /// <param name="value">The string label value.</param>
    public CborLabel(string value) : this((object)value ?? throw new ArgumentNullException(nameof(value))) { }

    /// <summary>
    /// Converts an integer to a <see cref="CborLabel"/>.
    /// </summary>
    /// <param name="value">The integer value to convert.</param>
    public static implicit operator CborLabel(int value)
    {
        return new(value);
    }

    /// <summary>
    /// Creates a <see cref="CborLabel"/> from an integer value.
    /// </summary>
    /// <param name="value">The integer value.</param>
    /// <returns>A new <see cref="CborLabel"/> containing the integer value.</returns>
    public static CborLabel FromInt32(int value)
    {
        return new(value);
    }

    /// <summary>
    /// Converts a long integer to a <see cref="CborLabel"/>.
    /// </summary>
    /// <param name="value">The long integer value to convert.</param>
    public static implicit operator CborLabel(long value)
    {
        return new(value);
    }

    /// <summary>
    /// Creates a <see cref="CborLabel"/> from a long integer value.
    /// </summary>
    /// <param name="value">The long integer value.</param>
    /// <returns>A new <see cref="CborLabel"/> containing the long integer value.</returns>
    public static CborLabel FromInt64(long value)
    {
        return new(value);
    }

    /// <summary>
    /// Converts a string to a <see cref="CborLabel"/>.
    /// </summary>
    /// <param name="value">The string value to convert.</param>
    public static implicit operator CborLabel(string value)
    {
        return new(value);
    }

    /// <summary>
    /// Creates a <see cref="CborLabel"/> from a string value.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <returns>A new <see cref="CborLabel"/> containing the string value.</returns>
    public static CborLabel FromString(string value)
    {
        return new(value);
    }
}

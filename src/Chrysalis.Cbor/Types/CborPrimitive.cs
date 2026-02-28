using System.Collections.ObjectModel;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Abstract union type representing any CBOR primitive value.
/// Used for scenarios where different primitive types must be stored type-safely.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record CborPrimitive : CborBase
{
    /// <summary>
    /// Converts an integer to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The integer value.</param>
    public static implicit operator CborPrimitive(int value)
    {
        return new CborInt(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from an integer value.
    /// </summary>
    /// <param name="value">The integer value.</param>
    /// <returns>A new <see cref="CborInt"/> instance.</returns>
    public static CborPrimitive FromInt32(int value)
    {
        return new CborInt(value);
    }

    /// <summary>
    /// Converts a long integer to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The long integer value.</param>
    public static implicit operator CborPrimitive(long value)
    {
        return new CborLong(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from a long integer value.
    /// </summary>
    /// <param name="value">The long integer value.</param>
    /// <returns>A new <see cref="CborLong"/> instance.</returns>
    public static CborPrimitive FromInt64(long value)
    {
        return new CborLong(value);
    }

    /// <summary>
    /// Converts an unsigned integer to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The unsigned integer value.</param>
    public static implicit operator CborPrimitive(uint value)
    {
        return new CborUInt(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from an unsigned integer value.
    /// </summary>
    /// <param name="value">The unsigned integer value.</param>
    /// <returns>A new <see cref="CborUInt"/> instance.</returns>
    public static CborPrimitive FromUInt32(uint value)
    {
        return new CborUInt(value);
    }

    /// <summary>
    /// Converts an unsigned long integer to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The unsigned long integer value.</param>
    public static implicit operator CborPrimitive(ulong value)
    {
        return new CborULong(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from an unsigned long integer value.
    /// </summary>
    /// <param name="value">The unsigned long integer value.</param>
    /// <returns>A new <see cref="CborULong"/> instance.</returns>
    public static CborPrimitive FromUInt64(ulong value)
    {
        return new CborULong(value);
    }

    /// <summary>
    /// Converts a string to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The string value.</param>
    public static implicit operator CborPrimitive(string value)
    {
        return new CborString(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from a string value.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <returns>A new <see cref="CborString"/> instance.</returns>
    public static CborPrimitive FromString(string value)
    {
        return new CborString(value);
    }

    /// <summary>
    /// Converts a boolean to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The boolean value.</param>
    public static implicit operator CborPrimitive(bool value)
    {
        return new CborBool(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from a boolean value.
    /// </summary>
    /// <param name="value">The boolean value.</param>
    /// <returns>A new <see cref="CborBool"/> instance.</returns>
    public static CborPrimitive FromBoolean(bool value)
    {
        return new CborBool(value);
    }

    /// <summary>
    /// Converts a byte array to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The byte array value.</param>
    public static implicit operator CborPrimitive(byte[] value)
    {
        return new CborBytes(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from a byte array value.
    /// </summary>
    /// <param name="value">The byte array value.</param>
    /// <returns>A new <see cref="CborBytes"/> instance.</returns>
    public static CborPrimitive FromBytes(byte[] value)
    {
        return new CborBytes(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from a byte array value.
    /// Satisfies CA2225 naming convention for the implicit byte[] operator.
    /// </summary>
    /// <param name="value">The byte array value.</param>
    /// <returns>A new <see cref="CborBytes"/> instance.</returns>
    public static CborPrimitive FromByteArray(byte[] value)
    {
        return new CborBytes(value);
    }

    /// <summary>
    /// Converts a float to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The float value.</param>
    public static implicit operator CborPrimitive(float value)
    {
        return new CborFloat(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from a float value.
    /// </summary>
    /// <param name="value">The float value.</param>
    /// <returns>A new <see cref="CborFloat"/> instance.</returns>
    public static CborPrimitive FromSingle(float value)
    {
        return new CborFloat(value);
    }

    /// <summary>
    /// Converts a double to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The double value.</param>
    public static implicit operator CborPrimitive(double value)
    {
        return new CborDouble(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from a double value.
    /// </summary>
    /// <param name="value">The double value.</param>
    /// <returns>A new <see cref="CborDouble"/> instance.</returns>
    public static CborPrimitive FromDouble(double value)
    {
        return new CborDouble(value);
    }

    /// <summary>
    /// Converts a collection of CBOR primitives to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The collection of CBOR primitive values.</param>
    public static implicit operator CborPrimitive(Collection<CborPrimitive> value)
    {
        return FromCollection(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from a collection of CBOR primitive values.
    /// </summary>
    /// <param name="value">The collection of CBOR primitive values.</param>
    /// <returns>A new <see cref="CborArray"/> instance.</returns>
    public static CborPrimitive FromCollection(IReadOnlyList<CborPrimitive> value)
    {
        return new CborArray(value);
    }

    /// <summary>
    /// Converts a dictionary of CBOR labels to primitives to a <see cref="CborPrimitive"/>.
    /// </summary>
    /// <param name="value">The dictionary of CBOR label-primitive pairs.</param>
    public static implicit operator CborPrimitive(Dictionary<CborLabel, CborPrimitive> value)
    {
        return new CborMap(value);
    }

    /// <summary>
    /// Creates a <see cref="CborPrimitive"/> from a dictionary of CBOR label-primitive pairs.
    /// </summary>
    /// <param name="value">The dictionary of CBOR label-primitive pairs.</param>
    /// <returns>A new <see cref="CborMap"/> instance.</returns>
    public static CborPrimitive FromDictionary(Dictionary<CborLabel, CborPrimitive> value)
    {
        return new CborMap(value);
    }
}

/// <summary>
/// A CBOR integer (int) primitive value.
/// </summary>
/// <param name="Value">The integer value.</param>
[CborSerializable]
public partial record CborInt(int Value) : CborPrimitive;

/// <summary>
/// A CBOR long integer (long) primitive value.
/// </summary>
/// <param name="Value">The long integer value.</param>
[CborSerializable]
public partial record CborLong(long Value) : CborPrimitive;

/// <summary>
/// A CBOR unsigned integer (uint) primitive value.
/// </summary>
/// <param name="Value">The unsigned integer value.</param>
[CborSerializable]
public partial record CborUInt(uint Value) : CborPrimitive;

/// <summary>
/// A CBOR unsigned long integer (ulong) primitive value.
/// </summary>
/// <param name="Value">The unsigned long integer value.</param>
[CborSerializable]
public partial record CborULong(ulong Value) : CborPrimitive;

/// <summary>
/// A CBOR text string primitive value.
/// </summary>
/// <param name="Value">The string value.</param>
[CborSerializable]
public partial record CborString(string Value) : CborPrimitive;

/// <summary>
/// A CBOR boolean primitive value.
/// </summary>
/// <param name="Value">The boolean value.</param>
[CborSerializable]
public partial record CborBool(bool Value) : CborPrimitive;

/// <summary>
/// A CBOR byte string primitive value.
/// </summary>
/// <param name="Value">The byte array value.</param>
[CborSerializable]
public partial record CborBytes(byte[] Value) : CborPrimitive;

/// <summary>
/// A CBOR single-precision floating-point primitive value.
/// </summary>
/// <param name="Value">The float value.</param>
[CborSerializable]
public partial record CborFloat(float Value) : CborPrimitive;

/// <summary>
/// A CBOR double-precision floating-point primitive value.
/// </summary>
/// <param name="Value">The double value.</param>
[CborSerializable]
public partial record CborDouble(double Value) : CborPrimitive;

/// <summary>
/// A CBOR null primitive value.
/// </summary>
/// <param name="IsNull">Whether this value represents null (always true).</param>
[CborSerializable]
public partial record CborNull(bool IsNull) : CborPrimitive
{
    /// <summary>
    /// Initializes a new instance of <see cref="CborNull"/> representing a null value.
    /// </summary>
    public CborNull() : this(true) { }

    /// <summary>
    /// Gets a singleton CBOR null value.
    /// </summary>
    public static CborPrimitive Null { get; } = new CborNull();
}

/// <summary>
/// A CBOR array of primitive values.
/// </summary>
/// <param name="Value">The read-only list of CBOR primitive elements.</param>
[CborSerializable]
public partial record CborArray(IReadOnlyList<CborPrimitive> Value) : CborPrimitive
{
    /// <summary>
    /// Initializes a new empty <see cref="CborArray"/>.
    /// </summary>
    public CborArray() : this([]) { }
}

/// <summary>
/// A CBOR map of label-primitive key-value pairs.
/// </summary>
/// <param name="Value">The dictionary of CBOR label keys to primitive values.</param>
[CborSerializable]
public partial record CborMap(Dictionary<CborLabel, CborPrimitive> Value) : CborPrimitive
{
    /// <summary>
    /// Initializes a new empty <see cref="CborMap"/>.
    /// </summary>
    public CborMap() : this([]) { }
}

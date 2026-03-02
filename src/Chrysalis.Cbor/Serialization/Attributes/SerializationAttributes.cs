using System.Globalization;

namespace Chrysalis.Cbor.Serialization.Attributes;

// Type-level attributes

/// <summary>
/// Marks a type for CBOR serialization and deserialization.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborSerializableAttribute : Attribute;

/// <summary>
/// Indicates that the type should be serialized as a CBOR map.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborMapAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CborMapAttribute"/> class.
    /// </summary>
    public CborMapAttribute() { }
}

/// <summary>
/// Indicates that the property or parameter is nullable in CBOR encoding.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborNullableAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CborNullableAttribute"/> class.
    /// </summary>
    public CborNullableAttribute() { }
}

/// <summary>
/// Indicates that the type should be serialized as a CBOR list (array).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborListAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CborListAttribute"/> class.
    /// </summary>
    public CborListAttribute() { }
}

/// <summary>
/// Marks a type as a CBOR union, enabling polymorphic deserialization.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborUnionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CborUnionAttribute"/> class.
    /// </summary>
    public CborUnionAttribute() { }
}

/// <summary>
/// Marks a type as a CBOR constructor encoding used in Plutus data.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborConstrAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CborConstrAttribute"/> class with any constructor index allowed.
    /// </summary>
    public CborConstrAttribute()
    {
        ConstructorIndex = -1;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CborConstrAttribute"/> class with the specified constructor index.
    /// </summary>
    /// <param name="constructorIndex">The Plutus constructor index.</param>
    public CborConstrAttribute(int constructorIndex)
    {
        ConstructorIndex = constructorIndex;
    }

    /// <summary>
    /// Gets the Plutus constructor index, or -1 if any index is allowed.
    /// </summary>
    public int ConstructorIndex { get; }

    /// <summary>
    /// Gets a value indicating whether any constructor index is accepted during deserialization.
    /// </summary>
    public bool IsAnyIndexAllowed => ConstructorIndex == -1;
}

/// <summary>
/// Specifies a CBOR tag to apply during serialization.
/// </summary>
/// <param name="tag">The CBOR tag value.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborTagAttribute(int tag) : Attribute
{
    /// <summary>
    /// Gets the CBOR tag value.
    /// </summary>
    public int Tag { get; } = tag;
}

/// <summary>
/// Specifies the discriminant index for a <see cref="CborListAttribute"/> type in a union.
/// The value corresponds to the first integer element of the CBOR array, enabling
/// probe-based union dispatch instead of try-catch fallback.
/// </summary>
/// <param name="index">The discriminant index value.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborIndexAttribute(int index) : Attribute
{
    /// <summary>
    /// Gets the discriminant index value.
    /// </summary>
    public int Index { get; } = index;
}

// Property/parameter attributes

/// <summary>
/// Specifies the serialization order for a property or parameter.
/// </summary>
/// <param name="order">The zero-based order index.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborOrderAttribute(int order) : Attribute
{
    /// <summary>
    /// Gets the serialization order index.
    /// </summary>
    public int Order { get; } = order;
}

/// <summary>
/// Specifies a map key for a property when serializing as a CBOR map.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborPropertyAttribute : Attribute
{
    /// <summary>
    /// Gets the string representation of the map key.
    /// </summary>
    public string StringKey { get; }

    /// <summary>
    /// Gets the original key value as a string.
    /// </summary>
    public string Key => StringKey;

    /// <summary>
    /// Gets the integer map key, if applicable.
    /// </summary>
    public int? IntKey { get; }

    /// <summary>
    /// Gets a value indicating whether the key is an integer.
    /// </summary>
    public bool IsIntKey { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CborPropertyAttribute"/> class with a string key.
    /// </summary>
    /// <param name="key">The string map key.</param>
    public CborPropertyAttribute(string key)
    {
        StringKey = key;
        IntKey = null;
        IsIntKey = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CborPropertyAttribute"/> class with an integer key.
    /// </summary>
    /// <param name="key">The integer map key.</param>
    public CborPropertyAttribute(int key)
    {
        StringKey = key.ToString(CultureInfo.InvariantCulture);
        IntKey = key;
        IsIntKey = true;
    }
}

/// <summary>
/// Specifies a fixed size constraint for the CBOR encoding.
/// </summary>
/// <param name="size">The expected size.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborSizeAttribute(int size) : Attribute
{
    /// <summary>
    /// Gets the expected size.
    /// </summary>
    public int Size { get; } = size;
}

/// <summary>
/// Indicates that the encoding should use indefinite length.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborIndefiniteAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CborIndefiniteAttribute"/> class.
    /// </summary>
    public CborIndefiniteAttribute() { }
}

/// <summary>
/// Indicates that the encoding should use definite length.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborDefiniteAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CborDefiniteAttribute"/> class.
    /// </summary>
    public CborDefiniteAttribute() { }
}

/// <summary>
/// Provides a hint for dispatching a union property based on a sibling field's value.
/// Apply multiple times to map different discriminant values to concrete types.
/// The discriminant property must be serialized before this property (lower CborOrder).
/// </summary>
/// <param name="discriminantProperty">The name of the sibling property whose value selects the concrete type.</param>
/// <param name="discriminantValue">The value of the sibling discriminant field that selects this type.</param>
/// <param name="concreteType">The concrete type to deserialize when the discriminant matches.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = true)]
public sealed class CborUnionHintAttribute(string discriminantProperty, int discriminantValue, Type concreteType) : Attribute
{
    /// <summary>
    /// Gets the name of the sibling property used as discriminant.
    /// </summary>
    public string DiscriminantProperty { get; } = discriminantProperty;

    /// <summary>
    /// Gets the discriminant value that selects this concrete type.
    /// </summary>
    public int DiscriminantValue { get; } = discriminantValue;

    /// <summary>
    /// Gets the concrete type to deserialize.
    /// </summary>
    public Type ConcreteType { get; } = concreteType;
}

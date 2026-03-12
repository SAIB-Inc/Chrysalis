using System.Globalization;

namespace Chrysalis.Codec.Serialization.Attributes;

/// <summary>
/// Marks a type for CBOR serialization code generation. Required on all types that participate
/// in CBOR serialization. Combine with <see cref="CborConstrAttribute"/>, <see cref="CborMapAttribute"/>,
/// <see cref="CborListAttribute"/>, or <see cref="CborUnionAttribute"/> to specify the encoding format.
/// <example>
/// <code>
/// [CborSerializable]
/// [CborConstr(0)]
/// public partial record MyDatum(long Amount, byte[] Owner) : CborRecord;
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborSerializableAttribute : Attribute;

/// <summary>
/// Encodes the type as a CBOR map (major type 5). Each property becomes a key-value pair.
/// Use <see cref="CborPropertyAttribute"/> on properties to specify map keys.
/// <example>
/// <code>
/// [CborSerializable]
/// [CborMap]
/// public partial record TransactionBody(
///     [CborProperty(0)] ICborMaybeIndefList&lt;TransactionInput&gt; Inputs,
///     [CborProperty(1)] ICborMaybeIndefList&lt;ITransactionOutput&gt; Outputs,
///     [CborProperty(2)] ulong Fee
/// ) : CborRecord;
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborMapAttribute : Attribute;

/// <summary>
/// Marks a property or parameter as nullable in CBOR serialization.
/// When the value is null, it is encoded as CBOR null rather than being omitted.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborNullableAttribute : Attribute;

/// <summary>
/// Encodes the type as a CBOR array (major type 4). Properties are encoded positionally.
/// <example>
/// <code>
/// [CborSerializable]
/// [CborList]
/// public partial record TransactionInput(ReadOnlyMemory&lt;byte&gt; TransactionId, uint Index) : CborRecord;
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborListAttribute : Attribute;

/// <summary>
/// Marks an interface or base type as a CBOR union (sum type). Concrete subtypes are
/// distinguished by their CBOR structure during deserialization. Use on the interface,
/// and mark each concrete type with its own encoding attribute.
/// <example>
/// <code>
/// [CborSerializable]
/// [CborUnion]
/// public interface IValue { }
///
/// [CborSerializable]
/// public partial record struct Lovelace(ulong Amount) : IValue;
///
/// [CborSerializable]
/// [CborList]
/// public partial record struct LovelaceWithMultiAsset(ulong Amount, MultiAssetOutput MultiAsset) : IValue;
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborUnionAttribute : Attribute;

/// <summary>
/// Encodes the type as a Plutus-style constructor (CBOR tag 121+ wrapping an array of fields).
/// Used for Plutus datum and redeemer types. The constructor index maps to the Plutus
/// constructor tag: index 0 = tag 121, index 1 = tag 122, etc.
/// Use index -1 (default constructor) to accept any constructor index during deserialization.
/// <example>
/// <code>
/// // Simple datum with constructor 0:
/// [CborSerializable]
/// [CborConstr(0)]
/// public partial record MyDatum(long Amount, PlutusBool IsActive) : CborRecord;
///
/// // Redeemer with constructor 1:
/// [CborSerializable]
/// [CborConstr(1)]
/// public partial record CancelRedeemer() : CborRecord;
///
/// // Union with multiple constructors:
/// [CborSerializable]
/// [CborUnion]
/// public interface IAction { }
///
/// [CborSerializable]
/// [CborConstr(0)]
/// public partial record Buy(long Price) : CborRecord, IAction;
///
/// [CborSerializable]
/// [CborConstr(1)]
/// public partial record Sell() : CborRecord, IAction;
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborConstrAttribute : Attribute
{
    public CborConstrAttribute() => ConstructorIndex = -1;
    public CborConstrAttribute(int constructorIndex) => ConstructorIndex = constructorIndex;
    public int ConstructorIndex { get; }
    public bool IsAnyIndexAllowed => ConstructorIndex == -1;
}

/// <summary>
/// Wraps the encoded value in a CBOR semantic tag (major type 6).
/// <example>
/// <code>
/// [CborSerializable]
/// [CborTag(30)]  // Rational number tag
/// [CborList]
/// public partial record CborRationalNumber(ulong Numerator, ulong Denominator) : CborRecord;
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class CborTagAttribute(int tag) : Attribute
{
    public int Tag { get; } = tag;
}

/// <summary>
/// Specifies the numeric index for union variant disambiguation.
/// Used with <see cref="CborUnionAttribute"/> when variants are identified by position.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CborIndexAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}

/// <summary>
/// Specifies the serialization order for a property or parameter within an array-encoded type.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

/// <summary>
/// Specifies the map key for a property in a <see cref="CborMapAttribute"/>-encoded type.
/// Accepts either a string key or integer key.
/// <example>
/// <code>
/// [CborSerializable]
/// [CborMap]
/// public partial record PostAlonzoTransactionOutput(
///     [CborProperty(0)] Address Address,
///     [CborProperty(1)] IValue Amount,
///     [CborProperty(2), CborNullable] IDatumOption? Datum,
///     [CborProperty(3), CborNullable] CborEncodedValue? ScriptRef
/// ) : CborRecord, ITransactionOutput;
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborPropertyAttribute : Attribute
{
    public string StringKey { get; }
    public string Key => StringKey;
    public int? IntKey { get; }
    public bool IsIntKey { get; }

    public CborPropertyAttribute(string key)
    {
        StringKey = key;
        IntKey = null;
        IsIntKey = false;
    }

    public CborPropertyAttribute(int key)
    {
        StringKey = key.ToString(CultureInfo.InvariantCulture);
        IntKey = key;
        IsIntKey = true;
    }
}

/// <summary>
/// Constrains a byte string property to a fixed size.
/// The serializer will validate that the byte array matches the expected length.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborSizeAttribute(int size) : Attribute
{
    public int Size { get; } = size;
}

/// <summary>
/// Forces indefinite-length encoding for arrays or byte strings (CBOR break-terminated).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborIndefiniteAttribute : Attribute;

/// <summary>
/// Forces definite-length encoding. Optionally specifies an explicit size for the container.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class CborDefiniteAttribute : Attribute
{
    public CborDefiniteAttribute() { }
    public CborDefiniteAttribute(int size) => Size = size;
    public int? Size { get; }
}

/// <summary>
/// Shorthand for <c>[CborSerializable] [CborConstr(N)]</c>. Marks a type as a Plutus data
/// constructor with the given index. Ideal for defining Plutus datums and redeemers.
/// <example>
/// <code>
/// // Instead of:
/// [CborSerializable]
/// [CborConstr(0)]
/// public partial record MyDatum(long Amount, byte[] Owner) : CborRecord;
///
/// // Write:
/// [PlutusData(0)]
/// public partial record MyDatum(long Amount, byte[] Owner) : CborRecord;
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class PlutusDataAttribute(int constructorIndex) : Attribute
{
    public int ConstructorIndex { get; } = constructorIndex;
}

/// <summary>
/// Provides hints for deserializing union-typed properties where the concrete type depends
/// on a sibling property's value.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false, AllowMultiple = true)]
public sealed class CborUnionHintAttribute(string discriminantProperty, int discriminantValue, Type concreteType) : Attribute
{
    public string DiscriminantProperty { get; } = discriminantProperty;
    public int DiscriminantValue { get; } = discriminantValue;
    public Type ConcreteType { get; } = concreteType;
}

using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Abstract union type representing an optional value in CBOR Plutus data encoding.
/// </summary>
/// <typeparam name="T">The type of the contained value.</typeparam>
[CborSerializable]
[CborUnion]
public abstract partial record CborOption<T> : CborBase { }

/// <summary>
/// Represents a present (Some) value in a <see cref="CborOption{T}"/>.
/// </summary>
/// <typeparam name="T">The type of the contained value.</typeparam>
/// <param name="Value">The contained value.</param>
[CborSerializable]
[CborConstr(0)]
[CborIndefinite]
public partial record Some<T>([CborOrder(0)] T Value) : CborOption<T>;

/// <summary>
/// Represents an absent (None) value in a <see cref="CborOption{T}"/>.
/// </summary>
/// <typeparam name="T">The type parameter of the absent option.</typeparam>
[CborSerializable]
[CborConstr(1)]
[CborDefinite]
public partial record None<T> : CborOption<T>;

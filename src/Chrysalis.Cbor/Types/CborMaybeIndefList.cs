using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Abstract base for CBOR lists that may use either definite or indefinite-length encoding.
/// </summary>
/// <typeparam name="T">The element type of the list.</typeparam>
[CborSerializable]
[CborUnion]
public abstract partial record CborMaybeIndefList<T> : CborBase { }

/// <summary>
/// A CBOR list using definite-length encoding.
/// </summary>
/// <typeparam name="T">The element type of the list.</typeparam>
/// <param name="Value">The list of elements.</param>
[CborSerializable]
public partial record CborDefList<T>(List<T> Value) : CborMaybeIndefList<T>;

/// <summary>
/// A CBOR list using indefinite-length encoding.
/// </summary>
/// <typeparam name="T">The element type of the list.</typeparam>
/// <param name="Value">The list of elements with indefinite encoding.</param>
[CborSerializable]
public partial record CborIndefList<T>([CborIndefinite] List<T> Value) : CborMaybeIndefList<T>;

/// <summary>
/// A CBOR list using definite-length encoding with CBOR tag 258 (set semantics).
/// </summary>
/// <typeparam name="T">The element type of the list.</typeparam>
/// <param name="Value">The list of elements.</param>
[CborSerializable]
[CborTag(258)]
public partial record CborDefListWithTag<T>(List<T> Value) : CborMaybeIndefList<T>;

/// <summary>
/// A CBOR list using indefinite-length encoding with CBOR tag 258 (set semantics).
/// </summary>
/// <typeparam name="T">The element type of the list.</typeparam>
/// <param name="Value">The list of elements with indefinite encoding.</param>
[CborSerializable]
[CborTag(258)]
public partial record CborIndefListWithTag<T>([CborIndefinite] List<T> Value) : CborMaybeIndefList<T>;

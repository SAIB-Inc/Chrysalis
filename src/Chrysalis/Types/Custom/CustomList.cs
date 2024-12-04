using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Custom;

/// <summary>
/// Custom type for a cbor definite list.
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="Value"></param>
[CborDefinite]
public record CborDefList<T>(List<T> Value) : CborList<T>(Value) where T : ICbor;

/// <summary>
/// Custom type for a nullable cbor indefinite list.
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="Value"></param>
public record CborMaybeIndefList<T>(CborList<T>? Value) : CborMaybe<CborList<T>>(Value) where T : ICbor;

/// <summary>
/// Custom type for a nullable cbor definite list.
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="Value"></param>
public record CborMaybeDefList<T>(CborDefList<T>? Value) : CborMaybe<CborDefList<T>>(Value) where T : ICbor;
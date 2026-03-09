namespace Chrysalis.Plutus.Types;

/// <summary>
/// A named variable binder with a textual name and a unique identifier.
/// Used in parsed UPLC text before DeBruijn conversion.
/// </summary>
public readonly record struct Name(string Text, int Unique);

/// <summary>
/// A DeBruijn-indexed variable binder. Index is 1-based:
/// 1 = nearest enclosing lambda, 2 = next outer, etc.
/// Used in serialized (Flat-encoded) UPLC programs.
/// </summary>
public readonly record struct DeBruijn(int Index);

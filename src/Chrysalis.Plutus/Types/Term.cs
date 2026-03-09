using System.Collections.Immutable;

namespace Chrysalis.Plutus.Types;

/// <summary>
/// A UPLC (Untyped Plutus Lambda Calculus) term, parameterized by binder type.
/// Use <see cref="Name"/> for parsed text, <see cref="DeBruijn"/> for serialized programs.
/// </summary>
public abstract record Term<TBinder>;

/// <summary>Variable reference.</summary>
public sealed record VarTerm<TBinder>(TBinder Name) : Term<TBinder>;

/// <summary>Lambda abstraction (anonymous function). Binds one variable in the body.</summary>
public sealed record LambdaTerm<TBinder>(TBinder Parameter, Term<TBinder> Body) : Term<TBinder>;

/// <summary>Function application: apply function to argument.</summary>
public sealed record ApplyTerm<TBinder>(Term<TBinder> Function, Term<TBinder> Argument) : Term<TBinder>;

/// <summary>A constant value (integer, bytestring, bool, data, etc.).</summary>
public sealed record ConstTerm<TBinder>(Constant Value) : Term<TBinder>;

/// <summary>Reference to a built-in function.</summary>
public sealed record BuiltinTerm<TBinder>(DefaultFunction Function) : Term<TBinder>;

/// <summary>Delayed computation — suspends evaluation of the inner term.</summary>
public sealed record DelayTerm<TBinder>(Term<TBinder> Body) : Term<TBinder>;

/// <summary>Force a delayed computation — triggers evaluation.</summary>
public sealed record ForceTerm<TBinder>(Term<TBinder> Body) : Term<TBinder>;

/// <summary>Construct a tagged value with fields (like a data constructor).</summary>
public sealed record ConstrTerm<TBinder>(ulong Tag, ImmutableArray<Term<TBinder>> Fields) : Term<TBinder>;

/// <summary>Case analysis on a constructor value. Selects a branch by constructor tag.</summary>
public sealed record CaseTerm<TBinder>(Term<TBinder> Scrutinee, ImmutableArray<Term<TBinder>> Branches) : Term<TBinder>;

/// <summary>Explicit error — immediately aborts evaluation.</summary>
public sealed record ErrorTerm<TBinder> : Term<TBinder>;

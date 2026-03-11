using System.Collections.Immutable;

namespace Chrysalis.Plutus.Types;

/// <summary>
/// Tag for O(1) dispatch in the CEK machine instead of type-check cascades.
/// </summary>
public enum TermTag
{
    Var = 0,
    Lambda = 1,
    Apply = 2,
    Const = 3,
    Builtin = 4,
    Delay = 5,
    Force = 6,
    Constr = 7,
    Case = 8,
    Error = 9
}

/// <summary>
/// A UPLC (Untyped Plutus Lambda Calculus) term, parameterized by binder type.
/// Use <see cref="Name"/> for parsed text, <see cref="DeBruijn"/> for serialized programs.
/// </summary>
public abstract record Term<TBinder>
{
    internal readonly TermTag TermTag;

    private protected Term(TermTag tag)
    {
        TermTag = tag;
    }
}

/// <summary>Variable reference.</summary>
public sealed record VarTerm<TBinder>(TBinder Name) : Term<TBinder>(TermTag.Var);

/// <summary>Lambda abstraction (anonymous function). Binds one variable in the body.</summary>
public sealed record LambdaTerm<TBinder>(TBinder Parameter, Term<TBinder> Body) : Term<TBinder>(TermTag.Lambda);

/// <summary>Function application: apply function to argument.</summary>
public sealed record ApplyTerm<TBinder>(Term<TBinder> Function, Term<TBinder> Argument) : Term<TBinder>(TermTag.Apply);

/// <summary>A constant value (integer, bytestring, bool, data, etc.).</summary>
public sealed record ConstTerm<TBinder>(Constant Value) : Term<TBinder>(TermTag.Const);

/// <summary>Reference to a built-in function.</summary>
public sealed record BuiltinTerm<TBinder>(DefaultFunction Function) : Term<TBinder>(TermTag.Builtin);

/// <summary>Delayed computation — suspends evaluation of the inner term.</summary>
public sealed record DelayTerm<TBinder>(Term<TBinder> Body) : Term<TBinder>(TermTag.Delay);

/// <summary>Force a delayed computation — triggers evaluation.</summary>
public sealed record ForceTerm<TBinder>(Term<TBinder> Body) : Term<TBinder>(TermTag.Force);

/// <summary>Construct a tagged value with fields (like a data constructor).</summary>
public sealed record ConstrTerm<TBinder>(ulong Tag, ImmutableArray<Term<TBinder>> Fields) : Term<TBinder>(TermTag.Constr);

/// <summary>Case analysis on a constructor value. Selects a branch by constructor tag.</summary>
public sealed record CaseTerm<TBinder>(Term<TBinder> Scrutinee, ImmutableArray<Term<TBinder>> Branches) : Term<TBinder>(TermTag.Case);

/// <summary>Explicit error — immediately aborts evaluation.</summary>
public sealed record ErrorTerm<TBinder>() : Term<TBinder>(TermTag.Error);

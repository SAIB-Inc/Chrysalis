using System.Collections.Immutable;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// CEK machine runtime value. Represents evaluated terms during execution.
/// </summary>
internal abstract record CekValue;

internal sealed record VConstant(Constant Value) : CekValue;

internal sealed record VLambda(DeBruijn Parameter, Term<DeBruijn> Body, Environment? Env) : CekValue;

internal sealed record VDelay(Term<DeBruijn> Body, Environment? Env) : CekValue;

internal sealed record VBuiltin(DefaultFunction Function, int Forces, ImmutableArray<CekValue> Args) : CekValue;

internal sealed record VConstr(ulong Index, ImmutableArray<CekValue> Fields) : CekValue;

/// <summary>
/// Linked-list environment for 1-based DeBruijn index lookup.
/// </summary>
internal sealed class Environment
{
    internal CekValue Value { get; }
    internal Environment? Next { get; }

    internal Environment(CekValue value, Environment? next)
    {
        Value = value;
        Next = next;
    }

    internal static CekValue? Lookup(Environment? env, int index)
    {
        Environment? current = env;
        int i = index;
        while (current is not null)
        {
            if (i == 1)
            {
                return current.Value;
            }

            i--;
            current = current.Next;
        }
        return null;
    }

    internal static Environment Extend(Environment? env, CekValue value) =>
        new(value, env);
}

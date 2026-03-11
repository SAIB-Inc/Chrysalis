using System.Runtime.CompilerServices;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// CEK machine runtime value. Record hierarchy for minimal per-variant allocation size.
/// </summary>
internal abstract record CekValue;

internal sealed record VConstant(Constant Value) : CekValue;

internal sealed record VLambda(DeBruijn Parameter, Term<DeBruijn> Body, Environment? Env) : CekValue;

internal sealed record VDelay(Term<DeBruijn> Body, Environment? Env) : CekValue;

internal sealed record VBuiltin(DefaultFunction Function, int Forces, CekValue[] Args, int ArgCount) : CekValue;

internal sealed record VConstr(ulong Index, CekValue[] Fields, int FieldCount) : CekValue;

/// <summary>
/// Linked-list environment for 1-based DeBruijn index lookup.
/// </summary>
internal sealed class Environment
{
    internal CekValue Value = null!;
    internal Environment? Next;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Environment Extend(Environment? env, CekValue value) => new() { Value = value, Next = env };
}

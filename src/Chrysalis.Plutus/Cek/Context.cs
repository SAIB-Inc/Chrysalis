using System.Collections.Immutable;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// Continuation frames for the CEK machine (spec Figure 2.10).
/// </summary>
internal abstract record Context;

internal sealed record NoFrame : Context
{
    internal static readonly NoFrame Instance = new();
}

internal sealed record FrameAwaitArg(CekValue Value, Context Ctx) : Context;

internal sealed record FrameAwaitFunTerm(Environment? Env, Term<DeBruijn> Term, Context Ctx) : Context;

internal sealed record FrameAwaitFunValue(CekValue Value, Context Ctx) : Context;

internal sealed record FrameForce(Context Ctx) : Context;

internal sealed record FrameConstr(
    Environment? Env,
    ulong Index,
    ImmutableArray<Term<DeBruijn>> Fields,
    int NextFieldIndex,
    ImmutableArray<CekValue> Resolved,
    Context Ctx
) : Context;

internal sealed record FrameCases(
    Environment? Env,
    ImmutableArray<Term<DeBruijn>> Branches,
    Context Ctx
) : Context;

internal static class ContextHelpers
{
    /// <summary>
    /// Push constructor fields onto the continuation stack as FrameAwaitFunValue frames.
    /// Used when case-matching on a constructor to apply its fields as arguments.
    /// </summary>
    internal static Context TransferArgStack(ImmutableArray<CekValue> fields, Context ctx)
    {
        Context c = ctx;
        for (int i = fields.Length - 1; i >= 0; i--)
        {
            c = new FrameAwaitFunValue(fields[i], c);
        }
        return c;
    }
}

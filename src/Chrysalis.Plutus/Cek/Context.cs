using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// Tag for context frame discriminated union.
/// </summary>
internal enum FrameTag : byte
{
    NoFrame,
    AwaitArg,
    AwaitFunTerm,
    AwaitFunValue,
    Force,
    Constr,
    Cases,
    AwaitArgDirect
}

/// <summary>
/// A single continuation frame. Uses a tag + shared fields to avoid per-frame heap allocation.
/// Stored in a pre-allocated stack array inside CekMachine.
/// </summary>
internal struct ContextFrame
{
    internal FrameTag Tag;

    // FrameAwaitArg, FrameAwaitFunValue: the value being held
    internal CekValue? Value;

    // FrameAwaitFunTerm: the pending term + environment
    internal Term<DeBruijn>? Term;
    internal Environment? Env;

    // FrameConstr
    internal ulong ConstrIndex;
    internal ImmutableArray<Term<DeBruijn>> ConstrFields;
    internal int NextFieldIndex;
    internal CekValue[]? Resolved;
    internal int ResolvedCount;

    // FrameCases
    internal ImmutableArray<Term<DeBruijn>> Branches;
}

/// <summary>
/// Stack-based continuation storage. Pre-allocated array avoids per-frame heap allocation.
/// </summary>
internal sealed class ContextStack
{
    private ContextFrame[] _frames;
    private int _top;

    internal ContextStack(int initialCapacity = 256)
    {
        _frames = new ContextFrame[initialCapacity];
        _top = 0;
    }

    internal bool IsEmpty => _top == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref ContextFrame Peek() => ref _frames[_top - 1];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal FrameTag PeekTag() => _top == 0 ? FrameTag.NoFrame : _frames[_top - 1].Tag;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref ContextFrame Pop() => ref _frames[--_top];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref ContextFrame Push()
    {
        if (_top >= _frames.Length)
        {
            Grow();
        }
        // Don't zero — caller sets Tag + relevant fields. Stale fields are harmless.
        return ref _frames[_top++];
    }

    internal void Drop()
    {
        if (_top > 0)
        {
            ref ContextFrame f = ref _frames[--_top];
            // Clear reference fields to allow GC (CekValue struct has references inside)
            f.Value = null;
            f.Term = null;
            f.Env = null;
            f.Resolved = null;
        }
    }

    internal void Reset()
    {
        // Clear reference fields to allow GC
        for (int i = 0; i < _top; i++)
        {
            ref ContextFrame f = ref _frames[i];
            f.Value = null;
            f.Term = null;
            f.Env = null;
            f.Resolved = null;
        }
        _top = 0;
    }

    private void Grow()
    {
        ContextFrame[] newFrames = new ContextFrame[_frames.Length * 2];
        _frames.AsSpan(0, _top).CopyTo(newFrames);
        _frames = newFrames;
    }

    /// <summary>
    /// Push constructor fields onto the stack as FrameAwaitFunValue frames (in reverse order).
    /// </summary>
    internal void TransferArgStack(CekValue[] fields, int fieldCount)
    {
        for (int i = fieldCount - 1; i >= 0; i--)
        {
            ref ContextFrame frame = ref Push();
            frame.Tag = FrameTag.AwaitFunValue;
            frame.Value = fields[i];
        }
    }
}

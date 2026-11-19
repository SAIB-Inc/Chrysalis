using System.Runtime.CompilerServices;

namespace Chrysalis.Tx.Builders;

internal sealed class BuildCount : IDisposable
{
    private static readonly AsyncLocal<StrongBox<int>?> Active = new();
    private readonly StrongBox<int>? _parent = Active.Value;
    private readonly StrongBox<int> _count = new();

    internal BuildCount() => Active.Value = _count;
    internal int Value => _count.Value;
    internal static void Increment() { if (Active.Value is { } count) { count.Value++; } }
    public void Dispose() => Active.Value = _parent;
}

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Chrysalis.Plutus.Types;

/// <summary>A mutable explicit stack backed by <see cref="ArrayPool{T}.Shared"/>; pass by <c>ref</c> and call <see cref="Dispose"/> in <c>finally</c>.</summary>
internal struct PooledStack<T>
{
    private T[]? _items;

    internal PooledStack(int capacity) => _items = ArrayPool<T>.Shared.Rent(capacity);

    internal int Count { get; private set; }

    internal readonly ref T Top => ref _items![Count - 1];

    internal void Push(T item)
    {
        if (Count == _items!.Length)
        {
            T[] next = ArrayPool<T>.Shared.Rent(_items.Length * 2);
            _items.AsSpan(0, Count).CopyTo(next);
            T[] old = _items;
            _items = next;
            Return(old, Count);
        }

        _items[Count++] = item;
    }

    internal T Pop()
    {
        int index = --Count;
        T item = _items![index];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _items[index] = default!;
        }

        return item;
    }

    internal readonly ReadOnlySpan<T> From(int start) => _items.AsSpan(start, Count - start);

    internal void Truncate(int count)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && count < Count)
        {
            _items.AsSpan(count, Count - count).Clear();
        }

        Count = count;
    }

    internal void Dispose()
    {
        if (_items is { } items)
        {
            int count = Count;
            _items = null;
            Count = 0;
            Return(items, count);
        }
    }

    private static void Return(T[] array, int count)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && count > 0)
        {
            array.AsSpan(0, count).Clear();
        }

        ArrayPool<T>.Shared.Return(array);
    }
}

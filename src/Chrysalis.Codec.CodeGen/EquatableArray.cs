using System.Collections;
using System.Collections.Immutable;

namespace Chrysalis.Codec.CodeGen;

// ImmutableArray equality compares storage identity; generator inputs need element equality.
internal readonly struct EquatableArray<T>(IEnumerable<T> values) : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
{
    private ImmutableArray<T> Items { get; } = [.. values];
    private ImmutableArray<T> NonDefaultItems => Items.IsDefault ? [] : Items;

    public int Count => NonDefaultItems.Length;
    public T this[int index] => NonDefaultItems[index];

    public bool Equals(EquatableArray<T> other) => NonDefaultItems.SequenceEqual(other.NonDefaultItems);
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (T item in NonDefaultItems)
        {
            hash = unchecked((hash * 31) + (item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item)));
        }
        return hash;
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public ImmutableArray<T>.Enumerator GetEnumerator() => NonDefaultItems.GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)NonDefaultItems).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();
}

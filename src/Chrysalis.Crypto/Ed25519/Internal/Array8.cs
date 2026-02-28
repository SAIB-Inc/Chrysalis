namespace Chrysalis.Crypto.Internal;

/// <summary>
/// A fixed-size struct containing 8 elements, used as a SHA-512 state representation.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal struct Array8<T>
{
    /// <summary>Element at index 0.</summary>
    internal T x0;
    /// <summary>Element at index 1.</summary>
    internal T x1;
    /// <summary>Element at index 2.</summary>
    internal T x2;
    /// <summary>Element at index 3.</summary>
    internal T x3;
    /// <summary>Element at index 4.</summary>
    internal T x4;
    /// <summary>Element at index 5.</summary>
    internal T x5;
    /// <summary>Element at index 6.</summary>
    internal T x6;
    /// <summary>Element at index 7.</summary>
    internal T x7;
}


using System;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// COSE Label type that can be either an integer or a text string.
/// Based on RFC 8152: label = int / tstr
/// </summary>
public partial record CborLabel : CborBase
{
    public object Value { get; }
    
    // Private constructor prevents object construction
    private CborLabel(object value) { Value = value; }
    
    // Public type-safe constructors
    public CborLabel(int value) : this((object)value) { }
    public CborLabel(long value) : this((object)value) { }
    public CborLabel(string value) : this((object)value ?? throw new ArgumentNullException(nameof(value))) { }
    
    // Implicit conversions for convenience
    public static implicit operator CborLabel(int value) => new(value);
    public static implicit operator CborLabel(long value) => new(value);
    public static implicit operator CborLabel(string value) => new(value);
}
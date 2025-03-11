using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
public abstract partial record CborBase
{
    public ReadOnlyMemory<byte>? Raw;

    public virtual void Write(CborWriter writer, List<object?> value, CborOptions options) { }
    public virtual object? Read(CborReader reader, CborOptions options) { throw new NotImplementedException(); }
}
using ChrysalisV2.Attributes;

namespace ChrysalisV2.Types.Core;

[CborSerializable(typeof(CborBool))]
public record CborBool(bool Value = false) : RawCbor
{
    public CborBool() : this(false) { }
}
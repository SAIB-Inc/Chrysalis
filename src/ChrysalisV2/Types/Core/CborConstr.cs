using ChrysalisV2.Attributes;

namespace ChrysalisV2.Types.Core;

[CborSerializable(typeof(CborConstr), 0)]
public record CborConstr() : RawCbor { }
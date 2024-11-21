using ChrysalisV2.Attributes;
using ChrysalisV2.Types.Core;

namespace ChrysalisV2.Types.Custom.Test;

[CborSerializable(typeof(CborBytes), isDefinite: true, size: 64)]
public record CborBoundedBytesTest(byte[] Value) : CborBytes(Value);
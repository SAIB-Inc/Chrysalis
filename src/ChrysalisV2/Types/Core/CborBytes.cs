using System.Drawing;
using ChrysalisV2.Attributes;

namespace ChrysalisV2.Types.Core;

[CborSerializable(typeof(CborBytes), isDefinite: false, size: int.MaxValue)]
public record CborBytes(byte[] Value) : RawCbor;
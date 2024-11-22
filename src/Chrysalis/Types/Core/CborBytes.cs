using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Core;

[CborSerializable(Converter = typeof(CborBytesConverter), IsDefinite = false, Size = int.MaxValue)]
public record CborBytes(byte[] Value) : RawCbor;
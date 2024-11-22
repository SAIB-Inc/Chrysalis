using Chrysalis.Attributes;
using Chrysalis.Converters;
using Chrysalis.Types.Core;

namespace Chrysalis.Types.Custom.Test;

[CborSerializable(Converter = typeof(CborBytesConverter), IsDefinite = true, Size = 64)]
public record CborBoundedBytesTest(byte[] Value) : CborBytes(Value);
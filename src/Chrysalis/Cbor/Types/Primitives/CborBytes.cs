using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;

namespace Chrysalis.Cbor.Types.Primitives;

[CborConverter(typeof(BytesConverter))]
public record CborBytes(ReadOnlyMemory<byte> Value) : CborBase;
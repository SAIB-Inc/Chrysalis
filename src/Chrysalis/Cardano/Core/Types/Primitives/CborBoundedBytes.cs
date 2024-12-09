using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cardano.Core.Types.Primitives;

[CborConverter(typeof(BytesConverter))]
[CborSize(64)]
public record CborBoundedBytes(byte[] Value) : CborBase;
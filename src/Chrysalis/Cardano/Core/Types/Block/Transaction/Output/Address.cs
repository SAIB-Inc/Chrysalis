using Chrysalis.Cbor.Abstractions;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

[CborConverter(typeof(BytesConverter))]
public record Address(ReadOnlyMemory<byte> Value) : CborBase;

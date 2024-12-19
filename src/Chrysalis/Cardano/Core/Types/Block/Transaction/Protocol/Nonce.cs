using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Protocol;


[CborConverter(typeof(UnionConverter))]
public abstract record Nonce : CborBase;

[CborConverter(typeof(CustomListConverter))]
public record NonceWithHash(
    [CborProperty(0)] CborUlong Variant,
    [CborProperty(1)] CborBytes? Hash
) : Nonce;

[CborConverter(typeof(CustomListConverter))]
public record NonceWithoutHash(
    [CborProperty(0)] CborUlong Variant
) : Nonce;
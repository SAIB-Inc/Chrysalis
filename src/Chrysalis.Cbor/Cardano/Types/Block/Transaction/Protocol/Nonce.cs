using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborConverter(typeof(UnionConverter))]
public abstract partial record Nonce : CborBase;

[CborConverter(typeof(CustomListConverter))]
public partial record NonceWithHash(
    [CborIndex(0)] CborUlong Variant,
    [CborIndex(1)] CborBytes? Hash
) : Nonce;

[CborConverter(typeof(CustomListConverter))]
public partial record NonceWithoutHash(
    [CborIndex(0)] CborUlong Variant
) : Nonce;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;

[CborSerializable]
[CborUnion]
public abstract partial record Nonce : CborBase<Nonce>
{
    [CborSerializable]
    [CborList]
    public partial record NonceWithHash(
        [CborIndex(0)] ulong Variant,
        [CborIndex(1)] byte[]? Hash
    ) : Nonce;

    [CborSerializable]
    [CborList]
    public partial record NonceWithoutHash(
        [CborIndex(0)] ulong Variant
    ) : Nonce;
}
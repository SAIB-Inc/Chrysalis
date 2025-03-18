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
        [CborOrder(0)] ulong Variant,
        [CborOrder(1)] byte[]? Hash
    ) : Nonce;

    [CborSerializable]
    [CborList]
    public partial record NonceWithoutHash(
        [CborOrder(0)] ulong Variant
    ) : Nonce;
}
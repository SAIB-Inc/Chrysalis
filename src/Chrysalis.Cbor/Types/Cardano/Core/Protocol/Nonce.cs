using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborUnion]
public abstract partial record Nonce : CborBase { }

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

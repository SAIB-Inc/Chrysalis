using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Protocol;

/// <summary>
/// Abstract base for nonce values used in the Cardano protocol for randomness.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Nonce : CborBase { }

/// <summary>
/// A nonce with a hash value, indicating a specific random seed.
/// </summary>
/// <param name="Variant">The nonce variant tag.</param>
/// <param name="Hash">The nonce hash value.</param>
[CborSerializable]
[CborUnionCase(1)]
[CborList]
public partial record NonceWithHash(
    [CborOrder(0)] ulong Variant,
    [CborOrder(1)] ReadOnlyMemory<byte>? Hash
) : Nonce;

/// <summary>
/// A nonce without a hash value, indicating the identity (neutral) nonce.
/// </summary>
/// <param name="Variant">The nonce variant tag.</param>
[CborSerializable]
[CborUnionCase(0)]
[CborList]
public partial record NonceWithoutHash(
    [CborOrder(0)] ulong Variant
) : Nonce;

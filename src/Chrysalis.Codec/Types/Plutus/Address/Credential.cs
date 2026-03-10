using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Plutus.Address;

/// <summary>
/// Abstract base for Plutus credential types used in addresses.
/// </summary>
[CborSerializable]
[CborUnion]
public partial interface ICredential : ICborType;

/// <summary>
/// A credential based on a verification key hash.
/// </summary>
[CborSerializable]
[CborConstr(0)]
public readonly partial record struct VerificationKey : ICredential
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> VerificationKeyHash { get; }
}

/// <summary>
/// A credential based on a script hash.
/// </summary>
[CborSerializable]
[CborConstr(1)]
public readonly partial record struct Script : ICredential
{
    [CborOrder(0)] public partial ReadOnlyMemory<byte> ScriptHash { get; }
}

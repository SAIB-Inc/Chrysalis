using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Plutus.Address;

/// <summary>
/// Abstract base for Plutus credential types used in addresses.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Credential : CborBase;

/// <summary>
/// A credential based on a verification key hash.
/// </summary>
/// <param name="VerificationKeyHash">The hash of the verification (public) key.</param>
[CborSerializable]
[CborConstr(0)]
public partial record VerificationKey([CborOrder(0)] byte[] VerificationKeyHash) : Credential;

/// <summary>
/// A credential based on a script hash.
/// </summary>
/// <param name="ScriptHash">The hash of the script.</param>
[CborSerializable]
[CborConstr(1)]
public partial record Script([CborOrder(0)] byte[] ScriptHash) : Credential;

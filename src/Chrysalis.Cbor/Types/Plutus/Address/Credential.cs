using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Plutus.Address;

[CborSerializable]
[CborUnion]
public abstract partial record Credential : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record VerificationKey([CborOrder(0)] byte[] VerificationKeyHash) : Credential;

[CborSerializable]
[CborConstr(1)]
public partial record Script([CborOrder(0)] byte[] ScriptHash) : Credential;
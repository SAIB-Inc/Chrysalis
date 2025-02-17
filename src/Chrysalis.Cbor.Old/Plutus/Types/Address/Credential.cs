using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Plutus.Types.Address;

[CborConverter(typeof(UnionConverter))]
public abstract record Credential : CborBase;


[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record VerificationKey(
    [CborProperty(0)] CborBytes VerificationKeyHash
) : Credential;


[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record Script(
    [CborProperty(0)] CborBytes ScriptHash
) : Credential;
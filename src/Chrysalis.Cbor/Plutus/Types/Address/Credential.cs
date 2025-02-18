using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Plutus.Types.Address;

[CborConverter(typeof(UnionConverter))]
public abstract record Credential : CborBase;


[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public record VerificationKey(
    [CborIndex(0)] CborBytes VerificationKeyHash
) : Credential;


[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public record Script(
    [CborIndex(0)] CborBytes ScriptHash
) : Credential;
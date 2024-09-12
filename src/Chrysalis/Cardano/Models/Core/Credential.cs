using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

// // Version 1
[CborSerializable(CborType.List)]
public record Credential( 
    [CborProperty(0)] CborInt CredentialType,
    [CborProperty(1)] CborBytes Hash
) : ICbor;

// [CborSerializable(CborType.Union)]
// [CborUnionTypes([
//     typeof(AddrKeyhash),
//     typeof(Scripthash),
// ])]
// public record Credential : ICbor;

// public record AddrKeyhash(
//     [CborProperty(0)] CborInt Tag,
//     [CborProperty(1)] CborBytes AddrKeyHash
// ) : Credential;

// public record Scripthash(
//     [CborProperty(0)] CborInt Tag,
//     [CborProperty(1)] CborBytes ScriptHash
// ) : Credential;
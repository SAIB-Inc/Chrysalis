using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(Signature)])]
public record MultisigScript : ICbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record Signature(CborBytes KeyHash) : MultisigScript;
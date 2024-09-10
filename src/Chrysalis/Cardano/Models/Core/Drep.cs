using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(DRepAddrKeyHash),
    typeof(DRepScriptHash),
    typeof(Abstain),
    typeof(NoConfidence),
])]
public record DRep : ICbor;

[CborSerializable(CborType.List)]
public record DRepAddrKeyHash(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes AddrKeyHash
) : DRep;

[CborSerializable(CborType.List)]
public record DRepScriptHash(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes ScriptHash
) : DRep;

[CborSerializable(CborType.List)]
public record Abstain(CborInt Tag) : DRep;

[CborSerializable(CborType.List)]
public record NoConfidence(CborInt Tag) : DRep;

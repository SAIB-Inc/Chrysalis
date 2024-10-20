using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Governance;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(DRepAddrKeyHash),
    typeof(DRepScriptHash),
    typeof(Abstain),
    typeof(DRepNoConfidence),
])]
public record DRep : RawCbor;

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
public record DRepNoConfidence(CborInt Tag) : DRep;

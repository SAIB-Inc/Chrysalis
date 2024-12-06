using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;

[CborConverter(typeof(UnionConverter))]
public abstract record DRep : CborBase;

[CborConverter(typeof(CustomListConverter))]
public record DRepAddrKeyHash(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes AddrKeyHash
) : DRep;

[CborConverter(typeof(CustomListConverter))]
public record DRepScriptHash(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes ScriptHash
) : DRep;

[CborConverter(typeof(CustomListConverter))]
public record Abstain(
    [CborProperty(0)] CborInt Tag
) : DRep;

[CborConverter(typeof(CustomListConverter))]
public record DRepNoConfidence(
    [CborProperty(0)] CborInt Tag
) : DRep;

using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(UnionConverter))]
public abstract record DRep : CborBase;

[CborConverter(typeof(CustomListConverter))]
public record DRepAddrKeyHash(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborBytes AddrKeyHash
) : DRep;

[CborConverter(typeof(CustomListConverter))]
public record DRepScriptHash(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborBytes ScriptHash
) : DRep;

[CborConverter(typeof(CustomListConverter))]
public record Abstain(
    [CborIndex(0)] CborInt Tag
) : DRep;

[CborConverter(typeof(CustomListConverter))]
public record DRepNoConfidence(
    [CborIndex(0)] CborInt Tag
) : DRep;

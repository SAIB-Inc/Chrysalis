using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(UnionConverter))]
public abstract partial record DRep : CborBase;

[CborConverter(typeof(CustomListConverter))]
public partial record DRepAddrKeyHash(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborBytes AddrKeyHash
) : DRep;

[CborConverter(typeof(CustomListConverter))]
public partial record DRepScriptHash(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborBytes ScriptHash
) : DRep;

[CborConverter(typeof(CustomListConverter))]
public partial record Abstain(
    [CborIndex(0)] CborInt Tag
) : DRep;

[CborConverter(typeof(CustomListConverter))]
public partial record DRepNoConfidence(
    [CborIndex(0)] CborInt Tag
) : DRep;

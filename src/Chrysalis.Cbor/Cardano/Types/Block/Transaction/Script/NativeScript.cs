using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

[CborConverter(typeof(UnionConverter))]
public abstract partial record NativeScript : CborBase;

[CborConverter(typeof(CustomListConverter))]
public partial record ScriptPubKey(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborBytes AddrKeyHash
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public partial record ScriptAll(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborDefList<NativeScript> Scripts
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public partial record ScriptAny(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborDefList<NativeScript> Scripts
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public partial record ScriptNOfK(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborInt N,
    [CborIndex(2)] CborDefList<NativeScript> Scripts
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public partial record InvalidBefore(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborUlong Slot
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public partial record InvalidHereafter(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborUlong Slot
) : NativeScript;

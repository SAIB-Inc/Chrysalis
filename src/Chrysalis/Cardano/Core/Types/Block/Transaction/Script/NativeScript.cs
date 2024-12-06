using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Script;

[CborConverter(typeof(UnionConverter))]
public abstract record NativeScript : CborBase;

[CborConverter(typeof(CustomListConverter))]
public record ScriptPubKey(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes AddrKeyHash
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public record ScriptAll(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborDefiniteList<NativeScript> Scripts
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public record ScriptAny(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborDefiniteList<NativeScript> Scripts
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public record ScriptNOfK(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborInt N,
    [CborProperty(2)] CborDefiniteList<NativeScript> Scripts
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public record InvalidBefore(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Slot
) : NativeScript;

[CborConverter(typeof(CustomListConverter))]
public record InvalidHereafter(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Slot
) : NativeScript;

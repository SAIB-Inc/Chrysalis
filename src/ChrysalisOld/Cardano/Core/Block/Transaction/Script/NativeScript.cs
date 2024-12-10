using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(ScriptPubKey),
    typeof(ScriptAll),
    typeof(ScriptAny),
    typeof(ScriptNOfK),
    typeof(InvalidBefore),
    typeof(InvalidHereafter)
])]
public record NativeScript : RawCbor;

[CborSerializable(CborType.List)]
public record ScriptPubKey(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes AddrKeyHash
) : NativeScript;

[CborSerializable(CborType.List)]
public record ScriptAll(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborDefiniteList<NativeScript> Scripts
) : NativeScript;

[CborSerializable(CborType.List)]
public record ScriptAny(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborDefiniteList<NativeScript> Scripts
) : NativeScript;

[CborSerializable(CborType.List)]
public record ScriptNOfK(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborInt N,
    [CborProperty(2)] CborDefiniteList<NativeScript> Scripts
) : NativeScript;

[CborSerializable(CborType.List)]
public record InvalidBefore(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Slot
) : NativeScript;

[CborSerializable(CborType.List)]
public record InvalidHereafter(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong Slot
) : NativeScript;

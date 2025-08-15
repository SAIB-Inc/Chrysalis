using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public abstract partial record NativeScript : CborBase { }

[CborSerializable]
[CborList]
public partial record ScriptPubKey(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] AddrKeyHash
) : NativeScript;

[CborSerializable]
[CborList]
public partial record ScriptAll(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] CborMaybeIndefList<NativeScript>? Scripts
) : NativeScript;

[CborSerializable]
[CborList]
public partial record ScriptAny(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] CborMaybeIndefList<NativeScript>? Scripts
) : NativeScript;

[CborSerializable]
[CborList]
public partial record ScriptNOfK(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] int N,
    [CborOrder(2)] CborMaybeIndefList<NativeScript>? Scripts
) : NativeScript;

[CborSerializable]
[CborList]
public partial record InvalidBefore(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Slot
) : NativeScript;

[CborSerializable]
[CborList]
public partial record InvalidHereafter(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Slot
) : NativeScript;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

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
    [CborOrder(1)] CborIndefList<NativeScript>? Scripts
) : NativeScript;

[CborSerializable]
[CborList]
public partial record ScriptAny(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] CborDefList<NativeScript>? Scripts
) : NativeScript;

[CborSerializable]
[CborList]
public partial record ScriptNOfK(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] int N,
    [CborOrder(2)] CborDefList<NativeScript>? Scripts
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
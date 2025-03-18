using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

[CborSerializable]
[CborUnion]
public abstract partial record NativeScript : CborBase<NativeScript>
{
    [CborSerializable]
    [CborList]
    public partial record ScriptPubKey(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] byte[] AddrKeyHash
    ) : NativeScript;

    [CborSerializable]
    [CborList]
    public partial record ScriptAll(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] CborMaybeIndefList<NativeScript>.CborDefList Scripts
    ) : NativeScript;

    [CborSerializable]
    [CborList]
    public partial record ScriptAny(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] CborMaybeIndefList<NativeScript>.CborDefList Scripts
    ) : NativeScript;

    [CborSerializable]
    [CborList]
    public partial record ScriptNOfK(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] int N,
        [CborIndex(2)] CborMaybeIndefList<NativeScript>.CborDefList Scripts
    ) : NativeScript;

    [CborSerializable]
    [CborList]
    public partial record InvalidBefore(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] ulong Slot
    ) : NativeScript;

    [CborSerializable]
    [CborList]
    public partial record InvalidHereafter(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] ulong Slot
    ) : NativeScript;
}
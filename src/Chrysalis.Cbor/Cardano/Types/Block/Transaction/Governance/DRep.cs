using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

// [CborSerializable]
[CborUnion]
public abstract partial record DRep : CborBase<DRep>
{
    // [CborSerializable]
    [CborList]
    public partial record DRepAddrKeyHash(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] byte[] AddrKeyHash
    ) : DRep;

    // [CborSerializable]
    [CborList]
    public partial record DRepScriptHash(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] byte[] ScriptHash
    ) : DRep;

    // [CborSerializable]
    [CborList]
    public partial record Abstain(
        [CborIndex(0)] int Tag
    ) : DRep;

    // [CborSerializable]
    [CborList]
    public partial record DRepNoConfidence(
        [CborIndex(0)] int Tag
    ) : DRep;

}

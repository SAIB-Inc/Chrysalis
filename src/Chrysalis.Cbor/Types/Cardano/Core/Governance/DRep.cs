using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Governance;

[CborSerializable]
[CborUnion]
public abstract partial record DRep : CborBase { }

[CborSerializable]
[CborList]
public partial record DRepAddrKeyHash(
[CborOrder(0)] int Tag,
[CborOrder(1)] byte[] AddrKeyHash
) : DRep;

[CborSerializable]
[CborList]
public partial record DRepScriptHash(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] ScriptHash
) : DRep;

[CborSerializable]
[CborList]
public partial record Abstain(
    [CborOrder(0)] int Tag
) : DRep;

[CborSerializable]
[CborList]
public partial record DRepNoConfidence(
    [CborOrder(0)] int Tag
) : DRep;


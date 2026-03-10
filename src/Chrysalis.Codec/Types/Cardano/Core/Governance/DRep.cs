using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Governance;

[CborSerializable]
[CborUnion]
public partial interface IDRep : ICborType;

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct DRepAddrKeyHash : IDRep
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> KeyHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct DRepScriptHash : IDRep
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> ScriptHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(2)]
public readonly partial record struct Abstain : IDRep
{
    [CborOrder(0)] public partial int Tag { get; }
}

[CborSerializable]
[CborList]
[CborIndex(3)]
public readonly partial record struct DRepNoConfidence : IDRep
{
    [CborOrder(0)] public partial int Tag { get; }
}

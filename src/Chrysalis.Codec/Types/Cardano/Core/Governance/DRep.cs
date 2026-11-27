using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Governance;

[CborSerializable]
[CborUnion(typeof(DRepAddrKeyHash), typeof(DRepScriptHash), typeof(Abstain), typeof(DRepNoConfidence))]
[Union]
public readonly partial struct DRep : ICborType;

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct DRepAddrKeyHash : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> KeyHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct DRepScriptHash : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> ScriptHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(2)]
public readonly partial record struct Abstain : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
}

[CborSerializable]
[CborList]
[CborIndex(3)]
public readonly partial record struct DRepNoConfidence : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
}

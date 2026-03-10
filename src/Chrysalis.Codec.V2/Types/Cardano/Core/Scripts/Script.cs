using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Scripts;

[CborSerializable]
[CborUnion]
public partial interface IScript : ICborType;

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct MultiSigScript : IScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial INativeScript NativeScript { get; }
}

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct PlutusV1Script : IScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> ScriptBytes { get; }
}

[CborSerializable]
[CborList]
[CborIndex(2)]
public readonly partial record struct PlutusV2Script : IScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> ScriptBytes { get; }
}

[CborSerializable]
[CborList]
[CborIndex(3)]
public readonly partial record struct PlutusV3Script : IScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> ScriptBytes { get; }
}

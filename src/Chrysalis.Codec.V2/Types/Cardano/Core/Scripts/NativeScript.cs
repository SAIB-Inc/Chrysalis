using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Scripts;

[CborSerializable]
[CborUnion]
public partial interface INativeScript : ICborType;

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct ScriptPubKey : INativeScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> KeyHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct ScriptAll : INativeScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ICborMaybeIndefList<INativeScript> Scripts { get; }
}

[CborSerializable]
[CborList]
[CborIndex(2)]
public readonly partial record struct ScriptAny : INativeScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ICborMaybeIndefList<INativeScript> Scripts { get; }
}

[CborSerializable]
[CborList]
[CborIndex(3)]
public readonly partial record struct ScriptNOfK : INativeScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial int N { get; }
    [CborOrder(2)] public partial ICborMaybeIndefList<INativeScript> Scripts { get; }
}

[CborSerializable]
[CborList]
[CborIndex(4)]
public readonly partial record struct InvalidBefore : INativeScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ulong Slot { get; }
}

[CborSerializable]
[CborList]
[CborIndex(5)]
public readonly partial record struct InvalidHereafter : INativeScript
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ulong Slot { get; }
}

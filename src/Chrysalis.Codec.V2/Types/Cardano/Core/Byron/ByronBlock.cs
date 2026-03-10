using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public readonly partial record struct ByronMainBlock : IBlock
{
    [CborOrder(0)] public partial ByronBlockHead Header { get; }
    [CborOrder(1)] public partial ByronBlockBody Body { get; }
    [CborOrder(2)] public partial CborEncodedValue Extra { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronEbBlock : IBlock
{
    [CborOrder(0)] public partial ByronEbbHead Header { get; }
    [CborOrder(1)] public partial CborEncodedValue Body { get; }
    [CborOrder(2)] public partial CborEncodedValue Extra { get; }
}

using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public readonly partial record struct ByronSlotId : ICborType
{
    [CborOrder(0)] public partial ulong Epoch { get; }
    [CborOrder(1)] public partial ulong Slot { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronBlockVersion : ICborType
{
    [CborOrder(0)] public partial int Major { get; }
    [CborOrder(1)] public partial int Minor { get; }
    [CborOrder(2)] public partial int Patch { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronSoftwareVersion : ICborType
{
    [CborOrder(0)] public partial string Name { get; }
    [CborOrder(1)] public partial ulong Number { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronTxProof : ICborType
{
    [CborOrder(0)] public partial ulong Index { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> MerkleRoot { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> WitnessHash { get; }
}

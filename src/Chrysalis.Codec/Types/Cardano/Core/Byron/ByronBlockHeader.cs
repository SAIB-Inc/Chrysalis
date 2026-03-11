using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public readonly partial record struct ByronBlockHead : ICborType
{
    [CborOrder(0)] public partial ulong ProtocolMagic { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> PrevBlock { get; }
    [CborOrder(2)] public partial ByronBlockProof BodyProof { get; }
    [CborOrder(3)] public partial ByronBlockCons ConsensusData { get; }
    [CborOrder(4)] public partial ByronBlockExtraData ExtraData { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronEbbHead : ICborType
{
    [CborOrder(0)] public partial ulong ProtocolMagic { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> PrevBlock { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> BodyProof { get; }
    [CborOrder(3)] public partial ByronEbbCons ConsensusData { get; }
    [CborOrder(4)] public partial CborEncodedValue ExtraData { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronBlockCons : ICborType
{
    [CborOrder(0)] public partial ByronSlotId SlotId { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> PublicKey { get; }
    [CborOrder(2)] public partial ICborMaybeIndefList<ulong> Difficulty { get; }
    [CborOrder(3)] public partial ByronBlockSig Signature { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronEbbCons : ICborType
{
    [CborOrder(0)] public partial ulong Epoch { get; }
    [CborOrder(1)] public partial ICborMaybeIndefList<ulong> Difficulty { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronBlockExtraData : ICborType
{
    [CborOrder(0)] public partial ByronBlockVersion BlockVersion { get; }
    [CborOrder(1)] public partial ByronSoftwareVersion SoftwareVersion { get; }
    [CborOrder(2)] public partial CborEncodedValue Attributes { get; }
    [CborOrder(3)] public partial CborEncodedValue ExtraProof { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronBlockProof : ICborType
{
    [CborOrder(0)] public partial ByronTxProof TxProof { get; }
    [CborOrder(1)] public partial CborEncodedValue SscProof { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> DlgProof { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte> UpdProof { get; }
}

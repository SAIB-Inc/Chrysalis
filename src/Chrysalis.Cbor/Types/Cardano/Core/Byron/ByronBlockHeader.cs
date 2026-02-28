using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public partial record ByronBlockHead(
    [CborOrder(0)] uint ProtocolMagic,
    [CborOrder(1)] byte[] PrevBlock,
    [CborOrder(2)] ByronBlockProof BodyProof,
    [CborOrder(3)] ByronBlockCons ConsensusData,
    [CborOrder(4)] ByronBlockHeadEx ExtraData
) : CborBase, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record ByronEbbHead(
    [CborOrder(0)] uint ProtocolMagic,
    [CborOrder(1)] byte[] PrevBlock,
    [CborOrder(2)] byte[] BodyProof,
    [CborOrder(3)] ByronEbbCons ConsensusData,
    [CborOrder(4)] CborMaybeIndefList<CborEncodedValue> ExtraData
) : CborBase, ICborPreserveRaw;

[CborSerializable]
[CborList]
public partial record ByronBlockCons(
    [CborOrder(0)] ByronSlotId SlotId,
    [CborOrder(1)] byte[] PubKey,
    [CborOrder(2)] CborMaybeIndefList<ulong> Difficulty,
    [CborOrder(3)] ByronBlockSig BlockSignature
) : CborBase;

[CborSerializable]
[CborList]
public partial record ByronEbbCons(
    [CborOrder(0)] ulong EpochId,
    [CborOrder(1)] CborMaybeIndefList<ulong> Difficulty
) : CborBase;

[CborSerializable]
[CborList]
public partial record ByronBlockHeadEx(
    [CborOrder(0)] ByronBlockVersion BlockVersion,
    [CborOrder(1)] ByronSoftwareVersion SoftwareVersion,
    [CborOrder(2)] CborEncodedValue? Attributes,
    [CborOrder(3)] byte[] ExtraProof
) : CborBase;

[CborSerializable]
[CborList]
public partial record ByronBlockProof(
    [CborOrder(0)] ByronTxProof TxProof,
    [CborOrder(1)] CborEncodedValue SscProof,
    [CborOrder(2)] byte[] DlgProof,
    [CborOrder(3)] byte[] UpdProof
) : CborBase;

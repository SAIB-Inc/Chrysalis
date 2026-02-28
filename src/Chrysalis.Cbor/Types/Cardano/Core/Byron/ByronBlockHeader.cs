using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Represents a Byron main block header containing protocol, consensus, and proof data.
/// </summary>
/// <param name="ProtocolMagic">The protocol magic number identifying the network.</param>
/// <param name="PrevBlock">The hash of the previous block.</param>
/// <param name="BodyProof">The proof of the block body contents.</param>
/// <param name="ConsensusData">The consensus-related data for this block.</param>
/// <param name="ExtraData">Extra header data including version information.</param>
[CborSerializable]
[CborList]
public partial record ByronBlockHead(
    [CborOrder(0)] uint ProtocolMagic,
    [CborOrder(1)] byte[] PrevBlock,
    [CborOrder(2)] ByronBlockProof BodyProof,
    [CborOrder(3)] ByronBlockCons ConsensusData,
    [CborOrder(4)] ByronBlockExtraData ExtraData
) : CborBase, ICborPreserveRaw;

/// <summary>
/// Represents a Byron epoch boundary block (EBB) header.
/// </summary>
/// <param name="ProtocolMagic">The protocol magic number identifying the network.</param>
/// <param name="PrevBlock">The hash of the previous block.</param>
/// <param name="BodyProof">The hash proof of the EBB body.</param>
/// <param name="ConsensusData">The EBB consensus data containing epoch and difficulty.</param>
/// <param name="ExtraData">Extra data encoded as CBOR values.</param>
[CborSerializable]
[CborList]
public partial record ByronEbbHead(
    [CborOrder(0)] uint ProtocolMagic,
    [CborOrder(1)] byte[] PrevBlock,
    [CborOrder(2)] byte[] BodyProof,
    [CborOrder(3)] ByronEbbCons ConsensusData,
    [CborOrder(4)] CborMaybeIndefList<CborEncodedValue> ExtraData
) : CborBase, ICborPreserveRaw;

/// <summary>
/// Represents Byron main block consensus data including slot, public key, and signature.
/// </summary>
/// <param name="SlotId">The slot identifier for this block.</param>
/// <param name="PubKey">The public key of the block issuer.</param>
/// <param name="Difficulty">The chain difficulty value.</param>
/// <param name="BlockSignature">The block signature.</param>
[CborSerializable]
[CborList]
public partial record ByronBlockCons(
    [CborOrder(0)] ByronSlotId SlotId,
    [CborOrder(1)] byte[] PubKey,
    [CborOrder(2)] CborMaybeIndefList<ulong> Difficulty,
    [CborOrder(3)] ByronBlockSig BlockSignature
) : CborBase;

/// <summary>
/// Represents Byron epoch boundary block consensus data.
/// </summary>
/// <param name="EpochId">The epoch number.</param>
/// <param name="Difficulty">The chain difficulty value.</param>
[CborSerializable]
[CborList]
public partial record ByronEbbCons(
    [CborOrder(0)] ulong EpochId,
    [CborOrder(1)] CborMaybeIndefList<ulong> Difficulty
) : CborBase;

/// <summary>
/// Represents extended header data for a Byron block including version info and attributes.
/// </summary>
/// <param name="BlockVersion">The block version (major.minor.patch).</param>
/// <param name="SoftwareVersion">The software version (name and number).</param>
/// <param name="Attributes">Optional encoded attributes.</param>
/// <param name="ExtraProof">The proof hash for the extra data.</param>
[CborSerializable]
[CborList]
public partial record ByronBlockExtraData(
    [CborOrder(0)] ByronBlockVersion BlockVersion,
    [CborOrder(1)] ByronSoftwareVersion SoftwareVersion,
    [CborOrder(2)] CborEncodedValue? Attributes,
    [CborOrder(3)] byte[] ExtraProof
) : CborBase;

/// <summary>
/// Represents the proof of a Byron block body including transaction, SSC, delegation, and update proofs.
/// </summary>
/// <param name="TxProof">The transaction proof.</param>
/// <param name="SscProof">The shared seed computation proof.</param>
/// <param name="DlgProof">The delegation proof hash.</param>
/// <param name="UpdProof">The update proof hash.</param>
[CborSerializable]
[CborList]
public partial record ByronBlockProof(
    [CborOrder(0)] ByronTxProof TxProof,
    [CborOrder(1)] CborEncodedValue SscProof,
    [CborOrder(2)] byte[] DlgProof,
    [CborOrder(3)] byte[] UpdProof
) : CborBase;

using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

/// <summary>
/// Represents a Byron slot identifier composed of an epoch and a slot within that epoch.
/// </summary>
/// <param name="Epoch">The epoch number.</param>
/// <param name="Slot">The slot number within the epoch.</param>
[CborSerializable]
[CborList]
public partial record ByronSlotId(
    [CborOrder(0)] ulong Epoch,
    [CborOrder(1)] ulong Slot
) : CborBase;

/// <summary>
/// Represents the Byron block protocol version as major.minor.patch.
/// </summary>
/// <param name="Major">The major version number.</param>
/// <param name="Minor">The minor version number.</param>
/// <param name="Patch">The patch version number.</param>
[CborSerializable]
[CborList]
public partial record ByronBlockVersion(
    [CborOrder(0)] int Major,
    [CborOrder(1)] int Minor,
    [CborOrder(2)] int Patch
) : CborBase;

/// <summary>
/// Represents the Byron software version with a name and build number.
/// </summary>
/// <param name="Name">The software name.</param>
/// <param name="Number">The software version number.</param>
[CborSerializable]
[CborList]
public partial record ByronSoftwareVersion(
    [CborOrder(0)] string Name,
    [CborOrder(1)] uint Number
) : CborBase;

/// <summary>
/// Represents the proof of Byron transactions including the index, Merkle root, and witness hash.
/// </summary>
/// <param name="Index">The transaction index.</param>
/// <param name="MerkleRoot">The Merkle root hash of the transactions.</param>
/// <param name="WitnessHash">The hash of the transaction witnesses.</param>
[CborSerializable]
[CborList]
public partial record ByronTxProof(
    [CborOrder(0)] uint Index,
    [CborOrder(1)] ReadOnlyMemory<byte> MerkleRoot,
    [CborOrder(2)] ReadOnlyMemory<byte> WitnessHash
) : CborBase;

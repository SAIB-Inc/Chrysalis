using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public partial record ByronSlotId(
    [CborOrder(0)] ulong Epoch,
    [CborOrder(1)] ulong Slot
) : CborBase;

[CborSerializable]
[CborList]
public partial record ByronBlockVersion(
    [CborOrder(0)] int Major,
    [CborOrder(1)] int Minor,
    [CborOrder(2)] int Patch
) : CborBase;

[CborSerializable]
[CborList]
public partial record ByronSoftwareVersion(
    [CborOrder(0)] string Name,
    [CborOrder(1)] uint Number
) : CborBase;

[CborSerializable]
[CborList]
public partial record ByronTxProof(
    [CborOrder(0)] uint Index,
    [CborOrder(1)] byte[] MerkleRoot,
    [CborOrder(2)] byte[] WitnessHash
) : CborBase;

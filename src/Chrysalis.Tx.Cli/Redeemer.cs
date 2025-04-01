using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Tx.Cli;
[CborSerializable]
[CborConstr(0)]
public partial record Indices(
    [CborOrder(0)] ulong InputIndex,
    [CborOrder(1)] OutputIndices OutputIndices
): CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record OutputIndices(
    [CborOrder(0)] ulong MainIndex,
    [CborOrder(1)] ulong FeeIndex,
    [CborOrder(1)] ulong ChangeIndex
) : CborBase;




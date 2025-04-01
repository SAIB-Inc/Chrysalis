using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Cli;
[CborSerializable]
[CborConstr(0)]
public partial record Indices(
    [CborOrder(0)] ulong InputIndex,
    [CborOrder(1)] CborIndefList<ulong> OutputIndices
): CborBase;






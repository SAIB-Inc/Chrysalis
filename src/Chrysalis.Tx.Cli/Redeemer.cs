using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Cli;
[CborSerializable]
[CborList]
public partial record Indices(
    [CborOrder(0)] int InputIndex,
    [CborOrder(1)] CborIndefList<int> OutputIndices
): CborBase;






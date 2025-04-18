using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborConstr(0)]
public partial record MintRedeemer(
    [CborOrder(0)]
    byte[] PolicyId,

    [CborOrder(1)]
    Option<int> GlobalParamsRefIndex,

    [CborOrder(2)]
    Option<IEnumerable<int>> MintOutputIndices
) : CborBase;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborConstr(0)]
public partial record DatumTag(
    [CborOrder(0)]
    byte[] OutrefHash
) : CborBase;
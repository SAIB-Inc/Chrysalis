using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Models.Cbor;

[CborSerializable]
[CborList]
public partial record ScriptRef(
    [CborOrder(0)] int Type,
    [CborOrder(1)] byte[] ScriptBytes
) : CborBase;

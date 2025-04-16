

using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborConstr(0)]
public partial record Outref(
    [CborOrder(0)] byte[] Id,
    [CborOrder(1)] ulong Index
): CborBase;
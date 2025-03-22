


using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;

namespace Chrysalis.Tx.Models;
[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ResolvedInput(
    [CborIndex(0)] TransactionInput Outref,
    [CborIndex(1)] TransactionOutput Output
) : CborBase;

using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;

[CborConverter(typeof(CustomListConverter))]
public partial record TransactionInput(
    [CborIndex(0)] CborBytes TransactionId,
    [CborIndex(1)] CborUlong Index
) : CborBase;
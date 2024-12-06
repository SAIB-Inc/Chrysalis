using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Input;

[CborConverter(typeof(CustomListConverter))]
public record TransactionInput(
    [CborProperty(0)] CborBytes TransactionId,
    [CborProperty(1)] CborUlong Index
) : CborBase;
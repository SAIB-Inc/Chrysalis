using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(CustomListConverter))]
public partial record GovActionId(
    [CborIndex(0)] CborBytes TransactionId,
    [CborIndex(1)] CborInt GovActionIndex
) : CborBase;
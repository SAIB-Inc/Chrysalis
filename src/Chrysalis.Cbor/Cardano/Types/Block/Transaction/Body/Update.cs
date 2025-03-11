using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;

[CborConverter(typeof(CustomListConverter))]
public partial record Update(
    [CborIndex(0)] ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates,
    [CborIndex(1)] CborUlong Epoch
) : CborBase;
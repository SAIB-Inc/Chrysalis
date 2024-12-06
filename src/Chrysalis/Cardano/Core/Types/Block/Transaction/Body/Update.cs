using Chrysalis.Cardano.Core.Types.Block.Transaction.Protocol;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Body;

[CborConverter(typeof(CustomListConverter))]
public record Update(
    [CborProperty(0)] ProposedProtocolParameterUpdates ProposedProtocolParameterUpdates,
    [CborProperty(1)] CborUlong Epoch
) : CborBase;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;

[CborConverter(typeof(CustomListConverter))]
public record ProposalProcedure(
    [CborProperty(0)] CborUlong Deposit,
    [CborProperty(1)] RewardAccount RewardAccount,
    [CborProperty(2)] GovAction GovAction,
    [CborProperty(3)] Anchor Anchor
) : CborBase;
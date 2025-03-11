using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;

[CborConverter(typeof(CustomListConverter))]
public partial record ProposalProcedure(
    [CborIndex(0)] CborUlong Deposit,
    [CborIndex(1)] RewardAccount RewardAccount,
    [CborIndex(2)] GovAction GovAction,
    [CborIndex(3)] Anchor Anchor
) : CborBase;
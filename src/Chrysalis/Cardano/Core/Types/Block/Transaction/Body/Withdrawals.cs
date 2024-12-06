using Chrysalis.Cardano.Core.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Body;

[CborConverter(typeof(MapConverter))]
public record Withdrawals(Dictionary<RewardAccount, CborUlong> Value) : CborBase;
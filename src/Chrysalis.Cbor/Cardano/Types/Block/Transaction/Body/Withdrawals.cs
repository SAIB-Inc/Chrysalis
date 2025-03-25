using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;

[CborSerializable]
public partial record Withdrawals(Dictionary<RewardAccount, ulong> Value) : CborBase;
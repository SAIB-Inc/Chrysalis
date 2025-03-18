using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;

[CborSerializable]
public partial record RewardAccount(byte[] Value) : CborBase<RewardAccount>;
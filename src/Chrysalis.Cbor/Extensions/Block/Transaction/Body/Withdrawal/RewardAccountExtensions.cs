using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.Withdrawal;

public static class RewardAccountExtensions
{
    public static byte[] Value(this RewardAccount self) => self.Value;
}
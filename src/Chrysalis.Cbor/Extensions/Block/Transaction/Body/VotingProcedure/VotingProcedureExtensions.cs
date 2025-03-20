using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using CVotingProcedure = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance.VotingProcedure;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.VotingProcedure;

public static class VotingProcedureExtensions
{
    public static int Vote(this CVotingProcedure self) => self.Vote;

    public static Anchor? Anchor(this CVotingProcedure self) => self.Anchor;
}
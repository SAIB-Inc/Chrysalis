using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using CVotingProcedure = Chrysalis.Cbor.Types.Cardano.Core.Governance.VotingProcedure;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.VotingProcedure;

public static class VotingProcedureExtensions
{
    public static int Vote(this CVotingProcedure self) => self.Vote;

    public static Anchor? Anchor(this CVotingProcedure self) => self.Anchor;
}
using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using CProposalProcedure = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance.ProposalProcedure;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.ProposalProcedure;

public static class ProposalProcedureExtensions
{
    public static ulong Deposit(this CProposalProcedure self) => self.Deposit;

    public static RewardAccount RewardAccount(this CProposalProcedure self) => self.RewardAccount;

    public static GovAction GovAction(this CProposalProcedure self) => self.GovAction;

    public static Anchor Anchor(this CProposalProcedure self) => self.Anchor;
}
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using CProposalProcedure = Chrysalis.Cbor.Types.Cardano.Core.Governance.ProposalProcedure;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Governance;

public static class ProposalProcedureExtensions
{
    public static ulong Deposit(this CProposalProcedure self) => self.Deposit;

    public static RewardAccount RewardAccount(this CProposalProcedure self) => self.RewardAccount;

    public static GovAction GovAction(this CProposalProcedure self) => self.GovAction;

    public static Anchor Anchor(this CProposalProcedure self) => self.Anchor;
}
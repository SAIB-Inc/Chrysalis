using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Governance;
using Chrysalis.Cardano.Models.Core.Certificates;
using Chrysalis.Cardano.Models.Plutus;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Map)]
public record TransactionBody(
    [CborProperty(0)] CborDefiniteList<TransactionInput> Inputs,
    [CborProperty(1)] CborDefiniteList<TransactionOutput> Outputs,
    [CborProperty(2)] CborUlong Fee,
    [CborProperty(3)] Option<CborUlong> TimeToLive,
    [CborProperty(4)] Option<CborDefiniteList<Certificate>> Certificates,
    [CborProperty(5)] Option<Withdrawals> Withdrawals,
    [CborProperty(7)] Option<CborBytes> AuxiliaryDataHash,
    [CborProperty(8)] Option<CborUlong> ValidityIntervalStart,
    [CborProperty(9)] Option<MultiAsset> Mint,
    [CborProperty(11)] Option<CborBytes> ScriptDataHash,
    [CborProperty(13)] Option<CborDefiniteList<TransactionInput>> Collateral,
    [CborProperty(14)] Option<CborDefiniteList<CborBytes>> RequiredSigners,
    [CborProperty(15)] Option<CborInt> NetworkId,
    [CborProperty(16)] Option<TransactionOutput> CollateralReturn,
    [CborProperty(17)] Option<CborUlong> TotalCollateral,    
    [CborProperty(18)] Option<CborDefiniteList<TransactionInput>> ReferenceInputs,
    [CborProperty(19)] Option<VotingProcedures> VotingProcedures, 
    [CborProperty(20)] Option<CborDefiniteList<ProposalProcedure>> ProposalProcedures,
    [CborProperty(21)] Option<CborUlong> TreasuryValue,
    [CborProperty(22)] Option<CborUlong> Donation 
) : ICbor;

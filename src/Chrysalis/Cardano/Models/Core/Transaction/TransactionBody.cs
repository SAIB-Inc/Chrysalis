using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Governance;
using Chrysalis.Cardano.Models.Core.Certificates;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Map)]
public record TransactionBody(
    [CborProperty(0)] CborDefiniteList<TransactionInput> Inputs,
    [CborProperty(1)] CborDefiniteList<TransactionOutput> Outputs,
    [CborProperty(2)] CborUlong Fee,
    [CborProperty(3)] CborUlong? TimeToLive,
    [CborProperty(4)] CborDefiniteList<Certificate>? Certificates,
    [CborProperty(5)] Withdrawals? Withdrawals,
    [CborProperty(7)] CborBytes? AuxiliaryDataHash,
    [CborProperty(8)] CborUlong? ValidityIntervalStart,
    [CborProperty(9)] MultiAsset? Mint,
    [CborProperty(11)] CborBytes? ScriptDataHash,
    [CborProperty(13)] CborDefiniteList<TransactionInput>? Collateral,
    [CborProperty(14)] CborDefiniteList<CborBytes>? RequiredSigners,
    [CborProperty(15)] CborInt? NetworkId,
    [CborProperty(16)] TransactionOutput? CollateralReturn,
    [CborProperty(17)] CborUlong? TotalCollateral,    
    [CborProperty(18)] CborDefiniteList<TransactionInput>? ReferenceInputs,
    [CborProperty(19)] VotingProcedures? VotingProcedures, 
    [CborProperty(20)] CborDefiniteList<ProposalProcedure>? ProposalProcedures,
    [CborProperty(21)] CborUlong? TreasuryValue,
    [CborProperty(22)] CborUlong? Donation 
) : ICbor;

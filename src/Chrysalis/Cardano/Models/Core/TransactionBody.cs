using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Map)]
public record TransactionBody(
    [CborProperty("inputs")] CborIndefiniteList<TransactionInput> Inputs,
    [CborProperty("outputs")] CborIndefiniteList<TransactionOutput> Outputs,
    [CborProperty("fee")] CborUlong Fee,
    [CborProperty("timeToLive")] CborUlong? TimeToLive,
    [CborProperty("certificates")] CborIndefiniteList<Certificate>? Certificates,
    [CborProperty("withdrawals")] CborMap<RewardAccount,CborUlong>? Withdrawals,
    [CborProperty("auxiliaryDataHash")] CborBytes? AuxiliaryDataHash,
    [CborProperty("validityIntervalStart")] CborUlong? ValidityIntervalStart,
    [CborProperty("mint")] MultiAsset? Mint,
    [CborProperty("scriptDataHash")] CborBytes? ScriptDataHash,
    [CborProperty("collateralInputs")] CborIndefiniteList<TransactionInput>? Collateral,
    [CborProperty("requiredSigners")] CborIndefiniteList<CborBytes>? RequiredSigners,
    [CborProperty("networkId")] CborInt? NetworkId,
    [CborProperty("collateralReturn")] TransactionOutput? CollateralReturn,
    [CborProperty("totalCollateral")] CborUlong? TotalCollateral,    
    [CborProperty("referenceInputs")] CborIndefiniteList<TransactionInput>? ReferenceInputs,
    [CborProperty("votingProcedures")] CborMap<Voter,CborMap<GovActionId,VotingProcedure>>? VotingProcedures, 
    [CborProperty("proposalProcedures")] ProposalProcedure? ProposalProcedures, //@TODO 
    [CborProperty("treasuryValue")] CborUlong? TreasuryValue,
    [CborProperty("donation")] CborUlong? Donation 
) : ICbor;

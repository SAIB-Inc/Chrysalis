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
    [CborProperty("mint")] CborBytes? Mint, //@TODO
    [CborProperty("scriptDataHash")] CborBytes? ScriptDataHash,
    [CborProperty("collateralInputs")] CborIndefiniteList<TransactionInput>? Collateral,
    [CborProperty("requiredSigners")] CborIndefiniteList<TransactionInput>? RequiredSigners, //@TODO
    [CborProperty("networkId")] CborInt? NetworkId,
    [CborProperty("collateralReturn")] TransactionOutput? CollateralReturn,
    [CborProperty("totalCollateral")] CborUlong? TotalCollateral,    
    [CborProperty("referenceInputs")] CborIndefiniteList<TransactionInput>? ReferenceInputs,
    [CborProperty("votingProcedures")] CborBytes? VotingProcedures, //@TODO  
    [CborProperty("proposalProcedures")] CborBytes? ProposalProcedures, //@TODO 
    [CborProperty("treasuryValue")] CborUlong? TreasuryValue,
    [CborProperty("donation")] CborUlong? Donation //@TODO 
) : ICbor;

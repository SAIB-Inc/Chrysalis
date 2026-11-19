using System.Text.Json.Serialization;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Represents the protocol parameters response from the Blockfrost API.
/// </summary>
public class BlockfrostProtocolParametersResponse
{
    /// <summary>Gets or sets the minimum fee coefficient A.</summary>
    [JsonPropertyName("min_fee_a")]
    public ulong? MinFeeA { get; set; }

    /// <summary>Gets or sets the minimum fee constant B.</summary>
    [JsonPropertyName("min_fee_b")]
    public ulong? MinFeeB { get; set; }

    /// <summary>Gets or sets the maximum block size in bytes.</summary>
    [JsonPropertyName("max_block_size")]
    public ulong? MaxBlockSize { get; set; }

    /// <summary>Gets or sets the maximum transaction size in bytes.</summary>
    [JsonPropertyName("max_tx_size")]
    public ulong? MaxTxSize { get; set; }

    /// <summary>Gets or sets the maximum block header size.</summary>
    [JsonPropertyName("max_block_header_size")]
    public ulong? MaxBlockHeaderSize { get; set; }

    /// <summary>Gets or sets the key deposit amount.</summary>
    [JsonPropertyName("key_deposit")]
    public ulong? KeyDeposit { get; set; }

    /// <summary>Gets or sets the pool deposit amount.</summary>
    [JsonPropertyName("pool_deposit")]
    public ulong? PoolDeposit { get; set; }

    /// <summary>Gets or sets the maximum epoch.</summary>
    [JsonPropertyName("e_max")]
    public ulong? EMax { get; set; }

    /// <summary>Gets or sets the desired number of pools.</summary>
    [JsonPropertyName("n_opt")]
    public ulong? NOpt { get; set; }

    /// <summary>Gets or sets the pool pledge influence factor.</summary>
    [JsonPropertyName("a0")]
    public decimal? A0 { get; set; }

    /// <summary>Gets or sets the monetary expansion rate.</summary>
    [JsonPropertyName("rho")]
    public decimal? Rho { get; set; }

    /// <summary>Gets or sets the treasury growth rate.</summary>
    [JsonPropertyName("tau")]
    public decimal? Tau { get; set; }

    /// <summary>Gets or sets the protocol major version.</summary>
    [JsonPropertyName("protocol_major_ver")]
    public ulong? ProtocolMajorVer { get; set; }

    /// <summary>Gets or sets the protocol minor version.</summary>
    [JsonPropertyName("protocol_minor_ver")]
    public ulong? ProtocolMinorVer { get; set; }

    /// <summary>Gets or sets the minimum pool cost.</summary>
    [JsonPropertyName("min_pool_cost")]
    public ulong? MinPoolCost { get; set; }

    /// <summary>Gets or sets the raw cost models dictionary.</summary>
    [JsonPropertyName("cost_models_raw")]
    public Dictionary<string, long[]>? CostModelsRaw { get; init; }

    /// <summary>Gets or sets the memory unit price.</summary>
    [JsonPropertyName("price_mem")]
    public decimal? PriceMem { get; set; }

    /// <summary>Gets or sets the step unit price.</summary>
    [JsonPropertyName("price_step")]
    public decimal? PriceStep { get; set; }

    /// <summary>Gets or sets the maximum transaction execution memory units.</summary>
    [JsonPropertyName("max_tx_ex_mem")]
    public ulong? MaxTxExMem { get; set; }

    /// <summary>Gets or sets the maximum transaction execution step units.</summary>
    [JsonPropertyName("max_tx_ex_steps")]
    public ulong? MaxTxExSteps { get; set; }

    /// <summary>Gets or sets the maximum block execution memory units.</summary>
    [JsonPropertyName("max_block_ex_mem")]
    public ulong? MaxBlockExMem { get; set; }

    /// <summary>Gets or sets the maximum block execution step units.</summary>
    [JsonPropertyName("max_block_ex_steps")]
    public ulong? MaxBlockExSteps { get; set; }

    /// <summary>Gets or sets the maximum value size.</summary>
    [JsonPropertyName("max_val_size")]
    public ulong? MaxValSize { get; set; }

    /// <summary>Gets or sets the collateral percentage.</summary>
    [JsonPropertyName("collateral_percent")]
    public ulong? CollateralPercent { get; set; }

    /// <summary>Gets or sets the maximum number of collateral inputs.</summary>
    [JsonPropertyName("max_collateral_inputs")]
    public ulong? MaxCollateralInputs { get; set; }

    /// <summary>Gets or sets the coins per UTxO byte size.</summary>
    [JsonPropertyName("coins_per_utxo_size")]
    public ulong? CoinsPerUtxoSize { get; set; }

    /// <summary>Gets or sets the minimum fee for reference script cost per byte.</summary>
    [JsonPropertyName("min_fee_ref_script_cost_per_byte")]
    public decimal? MinFeeRefScriptCostPerByte { get; set; }

    /// <summary>Gets or sets the stake pool no-confidence voting threshold.</summary>
    [JsonPropertyName("pvt_motion_no_confidence")]
    public decimal? PvtMotionNoConfidence { get; set; }

    /// <summary>Gets or sets the stake pool normal committee voting threshold.</summary>
    [JsonPropertyName("pvt_committee_normal")]
    public decimal? PvtCommitteeNormal { get; set; }

    /// <summary>Gets or sets the stake pool no-confidence committee voting threshold.</summary>
    [JsonPropertyName("pvt_committee_no_confidence")]
    public decimal? PvtCommitteeNoConfidence { get; set; }

    /// <summary>Gets or sets the stake pool hard-fork voting threshold.</summary>
    [JsonPropertyName("pvt_hard_fork_initiation")]
    public decimal? PvtHardForkInitiation { get; set; }

    /// <summary>Gets or sets the stake pool security parameter voting threshold.</summary>
    [JsonPropertyName("pvt_p_p_security_group")]
    public decimal? PvtPPSecurityGroup { get; set; }

    /// <summary>Gets or sets the DRep no-confidence voting threshold.</summary>
    [JsonPropertyName("dvt_motion_no_confidence")]
    public decimal? DvtMotionNoConfidence { get; set; }

    /// <summary>Gets or sets the DRep normal committee voting threshold.</summary>
    [JsonPropertyName("dvt_committee_normal")]
    public decimal? DvtCommitteeNormal { get; set; }

    /// <summary>Gets or sets the DRep no-confidence committee voting threshold.</summary>
    [JsonPropertyName("dvt_committee_no_confidence")]
    public decimal? DvtCommitteeNoConfidence { get; set; }

    /// <summary>Gets or sets the DRep constitution voting threshold.</summary>
    [JsonPropertyName("dvt_update_to_constitution")]
    public decimal? DvtUpdateToConstitution { get; set; }

    /// <summary>Gets or sets the DRep hard-fork voting threshold.</summary>
    [JsonPropertyName("dvt_hard_fork_initiation")]
    public decimal? DvtHardForkInitiation { get; set; }

    /// <summary>Gets or sets the DRep network parameter voting threshold.</summary>
    [JsonPropertyName("dvt_p_p_network_group")]
    public decimal? DvtPPNetworkGroup { get; set; }

    /// <summary>Gets or sets the DRep economic parameter voting threshold.</summary>
    [JsonPropertyName("dvt_p_p_economic_group")]
    public decimal? DvtPPEconomicGroup { get; set; }

    /// <summary>Gets or sets the DRep technical parameter voting threshold.</summary>
    [JsonPropertyName("dvt_p_p_technical_group")]
    public decimal? DvtPPTechnicalGroup { get; set; }

    /// <summary>Gets or sets the DRep governance parameter voting threshold.</summary>
    [JsonPropertyName("dvt_p_p_gov_group")]
    public decimal? DvtPPGovGroup { get; set; }

    /// <summary>Gets or sets the DRep treasury withdrawal voting threshold.</summary>
    [JsonPropertyName("dvt_treasury_withdrawal")]
    public decimal? DvtTreasuryWithdrawal { get; set; }

    /// <summary>Gets or sets the minimum constitutional committee size.</summary>
    [JsonPropertyName("committee_min_size")]
    public ulong? CommitteeMinSize { get; set; }

    /// <summary>Gets or sets the maximum constitutional committee term in epochs.</summary>
    [JsonPropertyName("committee_max_term_length")]
    public ulong? CommitteeMaxTermLength { get; set; }

    /// <summary>Gets or sets the governance action lifetime in epochs.</summary>
    [JsonPropertyName("gov_action_lifetime")]
    public ulong? GovActionLifetime { get; set; }

    /// <summary>Gets or sets the governance action deposit in lovelace.</summary>
    [JsonPropertyName("gov_action_deposit")]
    public ulong? GovActionDeposit { get; set; }

    /// <summary>Gets or sets the DRep registration deposit in lovelace.</summary>
    [JsonPropertyName("drep_deposit")]
    public ulong? DRepDeposit { get; set; }

    /// <summary>Gets or sets the DRep activity period in epochs.</summary>
    [JsonPropertyName("drep_activity")]
    public ulong? DRepActivity { get; set; }
}

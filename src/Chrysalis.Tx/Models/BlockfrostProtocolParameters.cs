using System.Text.Json.Serialization;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Represents the protocol parameters response from the Blockfrost API.
/// </summary>
public class BlockfrostProtocolParametersResponse
{
    /// <summary>Gets or sets the epoch number.</summary>
    [JsonPropertyName("epoch")]
    public int? Epoch { get; set; }

    /// <summary>Gets or sets the minimum fee coefficient A.</summary>
    [JsonPropertyName("min_fee_a")]
    public int? MinFeeA { get; set; }

    /// <summary>Gets or sets the minimum fee constant B.</summary>
    [JsonPropertyName("min_fee_b")]
    public int? MinFeeB { get; set; }

    /// <summary>Gets or sets the maximum block size in bytes.</summary>
    [JsonPropertyName("max_block_size")]
    public int? MaxBlockSize { get; set; }

    /// <summary>Gets or sets the maximum transaction size in bytes.</summary>
    [JsonPropertyName("max_tx_size")]
    public int? MaxTxSize { get; set; }

    /// <summary>Gets or sets the maximum block header size.</summary>
    [JsonPropertyName("max_block_header_size")]
    public int? MaxBlockHeaderSize { get; set; }

    /// <summary>Gets or sets the key deposit amount.</summary>
    [JsonPropertyName("key_deposit")]
    public string? KeyDeposit { get; set; }

    /// <summary>Gets or sets the pool deposit amount.</summary>
    [JsonPropertyName("pool_deposit")]
    public string? PoolDeposit { get; set; }

    /// <summary>Gets or sets the maximum epoch.</summary>
    [JsonPropertyName("e_max")]
    public int? EMax { get; set; }

    /// <summary>Gets or sets the desired number of pools.</summary>
    [JsonPropertyName("n_opt")]
    public int? NOpt { get; set; }

    /// <summary>Gets or sets the pool pledge influence factor.</summary>
    [JsonPropertyName("a0")]
    public double? A0 { get; set; }

    /// <summary>Gets or sets the monetary expansion rate.</summary>
    [JsonPropertyName("rho")]
    public double? Rho { get; set; }

    /// <summary>Gets or sets the treasury growth rate.</summary>
    [JsonPropertyName("tau")]
    public double? Tau { get; set; }

    /// <summary>Gets or sets the decentralisation parameter.</summary>
    [JsonPropertyName("decentralisation_param")]
    public double? DecentralisationParam { get; set; }

    /// <summary>Gets or sets the extra entropy.</summary>
    [JsonPropertyName("extra_entropy")]
    public object? ExtraEntropy { get; set; }

    /// <summary>Gets or sets the protocol major version.</summary>
    [JsonPropertyName("protocol_major_ver")]
    public int? ProtocolMajorVer { get; set; }

    /// <summary>Gets or sets the protocol minor version.</summary>
    [JsonPropertyName("protocol_minor_ver")]
    public int? ProtocolMinorVer { get; set; }

    /// <summary>Gets or sets the minimum UTxO value.</summary>
    [JsonPropertyName("min_utxo")]
    public string? MinUtxo { get; set; }

    /// <summary>Gets or sets the minimum pool cost.</summary>
    [JsonPropertyName("min_pool_cost")]
    public string? MinPoolCost { get; set; }

    /// <summary>Gets or sets the nonce.</summary>
    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }

    /// <summary>Gets or sets the cost models dictionary.</summary>
    [JsonPropertyName("cost_models")]
    public Dictionary<string, Dictionary<string, int>>? CostModels { get; init; }

    /// <summary>Gets or sets the raw cost models dictionary.</summary>
    [JsonPropertyName("cost_models_raw")]
    public Dictionary<string, int[]>? CostModelsRaw { get; init; }

    /// <summary>Gets or sets the memory unit price.</summary>
    [JsonPropertyName("price_mem")]
    public double PriceMem { get; set; }

    /// <summary>Gets or sets the step unit price.</summary>
    [JsonPropertyName("price_step")]
    public double PriceStep { get; set; }

    /// <summary>Gets or sets the maximum transaction execution memory units.</summary>
    [JsonPropertyName("max_tx_ex_mem")]
    public required string MaxTxExMem { get; set; }

    /// <summary>Gets or sets the maximum transaction execution step units.</summary>
    [JsonPropertyName("max_tx_ex_steps")]
    public required string MaxTxExSteps { get; set; }

    /// <summary>Gets or sets the maximum block execution memory units.</summary>
    [JsonPropertyName("max_block_ex_mem")]
    public required string MaxBlockExMem { get; set; }

    /// <summary>Gets or sets the maximum block execution step units.</summary>
    [JsonPropertyName("max_block_ex_steps")]
    public required string MaxBlockExSteps { get; set; }

    /// <summary>Gets or sets the maximum value size.</summary>
    [JsonPropertyName("max_val_size")]
    public required string MaxValSize { get; set; }

    /// <summary>Gets or sets the collateral percentage.</summary>
    [JsonPropertyName("collateral_percent")]
    public int CollateralPercent { get; set; }

    /// <summary>Gets or sets the maximum number of collateral inputs.</summary>
    [JsonPropertyName("max_collateral_inputs")]
    public int MaxCollateralInputs { get; set; }

    /// <summary>Gets or sets the coins per UTxO byte size.</summary>
    [JsonPropertyName("coins_per_utxo_size")]
    public required string CoinsPerUtxoSize { get; set; }

    /// <summary>Gets or sets the minimum fee for reference script cost per byte.</summary>
    [JsonPropertyName("min_fee_ref_script_cost_per_byte")]
    public int? MinFeeRefScriptCostPerByte { get; set; }
}

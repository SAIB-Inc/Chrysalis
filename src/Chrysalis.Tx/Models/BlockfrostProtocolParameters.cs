using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Chrysalis.Tx.Models;

public class BlockfrostProtocolParametersResponse
{
    [JsonPropertyName("epoch")]
    public int Epoch { get; set; }

    [JsonPropertyName("min_fee_a")]
    public int MinFeeA { get; set; }

    [JsonPropertyName("min_fee_b")]
    public int MinFeeB { get; set; }

    [JsonPropertyName("max_block_size")]
    public int MaxBlockSize { get; set; }

    [JsonPropertyName("max_tx_size")]
    public int MaxTxSize { get; set; }

    [JsonPropertyName("max_block_header_size")]
    public int MaxBlockHeaderSize { get; set; }

    [JsonPropertyName("key_deposit")]
    public required string KeyDeposit { get; set; }

    [JsonPropertyName("pool_deposit")]
    public required string PoolDeposit { get; set; }

    [JsonPropertyName("e_max")]
    public int EMax { get; set; }

    [JsonPropertyName("n_opt")]
    public int NOpt { get; set; }

    [JsonPropertyName("a0")]
    public double A0 { get; set; }

    [JsonPropertyName("rho")]
    public double Rho { get; set; }

    [JsonPropertyName("tau")]
    public double Tau { get; set; }

    [JsonPropertyName("decentralisation_param")]
    public double DecentralisationParam { get; set; }

    [JsonPropertyName("extra_entropy")]
    public required object ExtraEntropy { get; set; }

    [JsonPropertyName("protocol_major_ver")]
    public int ProtocolMajorVer { get; set; }

    [JsonPropertyName("protocol_minor_ver")]
    public int ProtocolMinorVer { get; set; }

    [JsonPropertyName("min_utxo")]
    public required string MinUtxo { get; set; }

    [JsonPropertyName("min_pool_cost")]
    public required string MinPoolCost { get; set; }

    [JsonPropertyName("nonce")]
    public required string Nonce { get; set; }

    [JsonPropertyName("cost_models")]
    public required Dictionary<string, Dictionary<string, int>> CostModels { get; set; }

    [JsonPropertyName("cost_models_raw")]
    public required Dictionary<string, int[]> CostModelsRaw { get; set; }

    [JsonPropertyName("price_mem")]
    public double PriceMem { get; set; }

    [JsonPropertyName("price_step")]
    public double PriceStep { get; set; }

    [JsonPropertyName("max_tx_ex_mem")]
    public required string MaxTxExMem { get; set; }

    [JsonPropertyName("max_tx_ex_steps")]
    public required string MaxTxExSteps { get; set; }

    [JsonPropertyName("max_block_ex_mem")]
    public required string MaxBlockExMem { get; set; }

    [JsonPropertyName("max_block_ex_steps")]
    public required string MaxBlockExSteps { get; set; }

    [JsonPropertyName("max_val_size")]
    public required string MaxValSize { get; set; }

    [JsonPropertyName("collateral_percent")]
    public int CollateralPercent { get; set; }

    [JsonPropertyName("max_collateral_inputs")]
    public int MaxCollateralInputs { get; set; }

    [JsonPropertyName("coins_per_utxo_size")]
    public required string CoinsPerUtxoSize { get; set; }

    [JsonPropertyName("min_fee_ref_script_cost_per_byte")]
    public int? MinFeeRefScriptCostPerByte { get; set; }
}

using System.Text.Json.Serialization;

namespace Chrysalis.Tx.Models;


public class BlockfrostUtxo
{
    [JsonPropertyName("address")]
    public string? Address { get; set; }
    
    [JsonPropertyName("tx_hash")]
    public string? TxHash { get; set; }
    
    [JsonPropertyName("tx_index")]
    public int? TxIndex { get; set; }
    
    [JsonPropertyName("output_index")]
    public int? OutputIndex { get; set; }
    
    [JsonPropertyName("amount")]
    public List<Amount>? Amount { get; set; }
    
    [JsonPropertyName("block")]
    public string? Block { get; set; }
    
    [JsonPropertyName("data_hash")]
    public string? DataHash { get; set; }
    
    [JsonPropertyName("inline_datum")]
    public string? InlineDatum { get; set; }
    
    [JsonPropertyName("reference_script_hash")]
    public string? ReferenceScriptHash { get; set; }
    
}

 public class Amount
    {
        [JsonPropertyName("unit")]
        public string? Unit { get; set; }
        
        [JsonPropertyName("quantity")]
        public string? Quantity { get; set; }
    }
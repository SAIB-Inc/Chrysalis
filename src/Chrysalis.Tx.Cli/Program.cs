using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Cli;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;

// Test transaction metadata retrieval
var blockfrost = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC", NetworkType.Preview);

// Test with a known transaction that has metadata
string txHash = "1d7ba2f9bb914d3457be9aec82cfaf1684c6705ae3baed0f60d846136395f1c1";

try
{
    // Test other endpoints first
    Console.WriteLine("Testing protocol parameters endpoint...");
    var protocolParams = await blockfrost.GetParametersAsync();
    Console.WriteLine($"Protocol params retrieved: MinFeeA={protocolParams.MinFeeA}");
    
    Console.WriteLine("\nTesting UTXO endpoint...");
    try 
    {
        var utxos = await blockfrost.GetUtxosAsync("addr_test1qplj3frty09zw07sn03ucr2al2p82akg5p2rws55ulrn3dzveufqne0w9me28c6ujmd7an7j980njdrntzz0gpsuuatqmjwand");
        Console.WriteLine($"UTXOs retrieved: {utxos.Count} UTXOs");
    }
    catch (Exception utxoEx)
    {
        Console.WriteLine($"UTXO test failed: {utxoEx.Message}");
    }
    
    Console.WriteLine($"\nTesting metadata endpoint for transaction: {txHash}");
    var metadata = await blockfrost.GetTransactionMetadataAsync(txHash);
    
    if (metadata != null)
    {
        Console.WriteLine("Metadata found:");
        foreach (var (label, metadatum) in metadata.Value)
        {
            Console.WriteLine($"  Label: {label}");
            DisplayMetadatum(metadatum, "    ");
        }
    }
    else
    {
        Console.WriteLine("No metadata found for this transaction.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

static void DisplayMetadatum(TransactionMetadatum metadatum, string indent = "")
{
    switch (metadatum)
    {
        case MetadataText text:
            Console.WriteLine($"{indent}Text: \"{text.Value}\"");
            break;
        case MetadatumIntLong intLong:
            Console.WriteLine($"{indent}Integer: {intLong.Value}");
            break;
        case MetadatumIntUlong intUlong:
            Console.WriteLine($"{indent}UInteger: {intUlong.Value}");
            break;
        case MetadatumBytes bytes:
            Console.WriteLine($"{indent}Bytes: {Convert.ToHexString(bytes.Value)}");
            break;
        case MetadatumList list:
            Console.WriteLine($"{indent}List ({list.Value.Count} items):");
            for (int i = 0; i < list.Value.Count; i++)
            {
                Console.WriteLine($"{indent}  [{i}]:");
                DisplayMetadatum(list.Value[i], indent + "    ");
            }
            break;
        case MetadatumMap map:
            Console.WriteLine($"{indent}Map ({map.Value.Count} entries):");
            foreach (var (key, value) in map.Value)
            {
                Console.WriteLine($"{indent}  Key:");
                DisplayMetadatum(key, indent + "    ");
                Console.WriteLine($"{indent}  Value:");
                DisplayMetadatum(value, indent + "    ");
            }
            break;
        default:
            Console.WriteLine($"{indent}Unknown type: {metadatum.GetType().Name}");
            break;
    }
}

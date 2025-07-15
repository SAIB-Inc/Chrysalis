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
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

// Test Kupo provider
Console.WriteLine("=== Testing Kupo Provider ===\n");

var kupo = new Kupo("https://kupo1vqn9ql3n5srn2me0nqz.preview-v2.kupo-m1.demeter.run", NetworkType.Preview);
var testAddress = "addr_test1wruqu6yj05wwpf2gew53w4t354rgghhuud83wf8qa4sgc5gnfwaxc";

Console.WriteLine($"Network: {kupo.NetworkType}");
Console.WriteLine($"Test address: {testAddress}\n");

try
{
    // Test protocol parameters
    Console.WriteLine("1. Testing GetParametersAsync...");
    var protocolParams = await kupo.GetParametersAsync();
    Console.WriteLine($"✅ Protocol params retrieved:");
    Console.WriteLine($"   - MinFeeA: {protocolParams.MinFeeA}");
    Console.WriteLine($"   - MinFeeB: {protocolParams.MinFeeB}");
    Console.WriteLine($"   - MaxTxSize: {protocolParams.MaxTransactionSize}");
    Console.WriteLine($"   - KeyDeposit: {protocolParams.KeyDeposit}");
    Console.WriteLine($"   - PoolDeposit: {protocolParams.PoolDeposit}\n");

    // Test UTXOs
    Console.WriteLine("2. Testing GetUtxosAsync...");
    var utxos = await kupo.GetUtxosAsync([testAddress]);
    Console.WriteLine($"✅ Found {utxos.Count} UTXOs:");

    foreach (var (utxo, index) in utxos.Select((u, i) => (u, i)))
    {
        Console.WriteLine($"\n   UTXO #{index + 1}:");
        Console.WriteLine($"   - TX: {Convert.ToHexString(utxo.Outref.TransactionId).ToLower()}#{utxo.Outref.Index}");

        var value = utxo.Output.Amount();
        if (value is Lovelace lovelace)
        {
            Console.WriteLine($"   - Value: {lovelace.Value:N0} lovelace");
        }
        else if (value is LovelaceWithMultiAsset multiAsset)
        {
            Console.WriteLine($"   - Value: {multiAsset.LovelaceValue.Value:N0} lovelace + assets");
            if (multiAsset.MultiAsset?.Value != null)
            {
                foreach (var (policyId, assets) in multiAsset.MultiAsset.Value)
                {
                    Console.WriteLine($"     Policy: {Convert.ToHexString(policyId).ToLower()}");
                    foreach (var (assetName, amount) in assets.Value)
                    {
                        var nameStr = assetName.Length > 0 ? Convert.ToHexString(assetName) : "(empty)";
                        Console.WriteLine($"       {nameStr}: {amount}");
                    }
                }
            }
        }

        if (utxo.Output.DatumOption() != null)
        {
            Console.WriteLine($"   - Datum: {utxo.Output.DatumOption()?.GetType().Name}");
        }

        if (utxo.Output.ScriptRef() != null)
        {
            Console.WriteLine($"   - Script Reference: Present");
        }
    }

    // Test methods that should throw NotImplementedException
    Console.WriteLine("\n3. Testing unsupported methods...");

    try
    {
        await kupo.SubmitTransactionAsync(null!);
    }
    catch (NotImplementedException ex)
    {
        Console.WriteLine($"✅ SubmitTransactionAsync correctly throws: {ex.Message}");
    }

    try
    {
        await kupo.GetTransactionMetadataAsync("abc123");
    }
    catch (NotImplementedException ex)
    {
        Console.WriteLine($"✅ GetTransactionMetadataAsync correctly throws: {ex.Message}");
    }

    Console.WriteLine("\n✅ All tests passed!");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    }
}

return;

// Comment out original metadata test
/*
// Test transaction metadata retrieval
var blockfrost = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC", NetworkType.Preview);

// Test with a known transaction that has metadata
string txHash = args.Length > 0 ? args[0] : "1d7ba2f9bb914d3457be9aec82cfaf1684c6705ae3baed0f60d846136395f1c1";

try
{
    var allPayloadBytes = new List<byte>();
    string currentTxHash = txHash;
    
    Console.WriteLine($"Starting metadata collection from transaction: {currentTxHash}");
    
    while (!string.IsNullOrEmpty(currentTxHash))
    {
        Console.WriteLine($"Getting metadata for: {currentTxHash}");
        var metadata = await blockfrost.GetTransactionMetadataAsync(currentTxHash);
        
        if (metadata == null)
        {
            Console.WriteLine("No metadata found for this transaction.");
            break;
        }
        
        string? nextHash = null;
        
        foreach (var (label, metadatum) in metadata.Value)
        {
            Console.WriteLine($"Processing label: {label}");
            
            if (metadatum is MetadatumMap map)
            {
                // Look for "next" field
                foreach (var (key, value) in map.Value)
                {
                    if (key is MetadataText keyText && keyText.Value == "next" && value is MetadataText nextText)
                    {
                        nextHash = nextText.Value;
                        if (nextHash.StartsWith("0x"))
                            nextHash = nextHash[2..];
                        Console.WriteLine($"Found next hash: {nextHash}");
                    }
                    
                    // Look for "payload" field
                    if (key is MetadataText payloadKey && payloadKey.Value == "payload" && value is MetadatumList payloadList)
                    {
                        Console.WriteLine($"Found payload with {payloadList.Value.Count} chunks");
                        
                        foreach (var chunk in payloadList.Value)
                        {
                            if (chunk is MetadataText chunkText)
                            {
                                string hexData = chunkText.Value;
                                if (hexData.StartsWith("0x"))
                                    hexData = hexData[2..];
                                
                                try
                                {
                                    byte[] chunkBytes = Convert.FromHexString(hexData);
                                    allPayloadBytes.AddRange(chunkBytes);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error converting hex chunk: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }
        
        currentTxHash = nextHash;
    }
    
    Console.WriteLine($"Collected {allPayloadBytes.Count} total bytes");
    
    // Write to file
    string outputPath = "/tmp/hello_adafs.png";
    await File.WriteAllBytesAsync(outputPath, allPayloadBytes.ToArray());
    
    Console.WriteLine($"File written to: {outputPath}");
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
*/

using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;

// Test transaction metadata retrieval
using Blockfrost blockfrost = new("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC", NetworkType.Preview);

// Test with a known transaction that has metadata
string txHash = args.Length > 0 ? args[0] : "1d7ba2f9bb914d3457be9aec82cfaf1684c6705ae3baed0f60d846136395f1c1";

try
{
    List<byte> allPayloadBytes = [];
    string currentTxHash = txHash;

    Console.WriteLine($"Starting metadata collection from transaction: {currentTxHash}");

    while (!string.IsNullOrEmpty(currentTxHash))
    {
        Console.WriteLine($"Getting metadata for: {currentTxHash}");
        Metadata? metadata = await blockfrost.GetTransactionMetadataAsync(currentTxHash).ConfigureAwait(false);

        if (metadata == null)
        {
            Console.WriteLine("No metadata found for this transaction.");
            break;
        }

        string? nextHash = null;

        foreach ((ulong label, TransactionMetadatum? metadatum) in metadata.Value)
        {
            Console.WriteLine($"Processing label: {label}");

            if (metadatum is MetadatumMap map)
            {
                // Look for "next" field
                foreach ((TransactionMetadatum? key, TransactionMetadatum? value) in map.Value)
                {
                    if (key is MetadataText keyText && keyText.Value == "next" && value is MetadataText nextText)
                    {
                        nextHash = nextText.Value;
                        if (nextHash.StartsWith("0x", StringComparison.Ordinal))
                        {
                            nextHash = nextHash[2..];
                        }

                        Console.WriteLine($"Found next hash: {nextHash}");
                    }

                    // Look for "payload" field
                    if (key is MetadataText payloadKey && payloadKey.Value == "payload" && value is MetadatumList payloadList)
                    {
                        Console.WriteLine($"Found payload with {payloadList.Value.Count} chunks");

                        foreach (TransactionMetadatum chunk in payloadList.Value)
                        {
                            if (chunk is MetadataText chunkText)
                            {
                                string hexData = chunkText.Value;
                                if (hexData.StartsWith("0x", StringComparison.Ordinal))
                                {
                                    hexData = hexData[2..];
                                }

                                try
                                {
                                    byte[] chunkBytes = Convert.FromHexString(hexData);
                                    allPayloadBytes.AddRange(chunkBytes);
                                }
                                catch (FormatException ex)
                                {
                                    Console.WriteLine($"Error converting hex chunk: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }

        currentTxHash = nextHash!;
    }

    Console.WriteLine($"Collected {allPayloadBytes.Count} total bytes");

    // Write to file
    string outputPath = "/tmp/hello_adafs.png";
    await File.WriteAllBytesAsync(outputPath, [.. allPayloadBytes]).ConfigureAwait(false);

    Console.WriteLine($"File written to: {outputPath}");
}
catch (InvalidOperationException ex)
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
            foreach ((TransactionMetadatum? key, TransactionMetadatum? value) in map.Value)
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

using Dahomey.Cbor.Serialization;
using System.Text;
using System.Text.Json;
using Chrysalis.Wallet.CIPs.CIP8.Builders;
using Chrysalis.Wallet.CIPs.CIP8.Extensions;
using Chrysalis.Wallet.CIPs.CIP8.Models;
using Chrysalis.Wallet.CIPs.CIP8.Signers;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;

if (args.Length == 0)
{
    ShowHelp();
    return 0;
}

try
{
    switch (args[0].ToUpperInvariant())
    {
        case "SIGN":
            return await HandleSign([.. args.Skip(1)]).ConfigureAwait(false);
        case "VERIFY":
            return await HandleVerify([.. args.Skip(1)]).ConfigureAwait(false);
        case "HELP":
        case "--HELP":
        case "-H":
            ShowHelp();
            return 0;
        default:
            Console.WriteLine($"Unknown command: {args[0]}");
            ShowHelp();
            return 1;
    }
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}

static void ShowHelp()
{
    Console.WriteLine("Chrysalis Wallet CLI - Sign and verify messages using CIP-8");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run sign --skey <file> --vkey <file> --payload <file> [options]");
    Console.WriteLine("  dotnet run verify --signature <sig> --vkey <file> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  sign     Sign a message using CIP-8 format");
    Console.WriteLine("  verify   Verify a CIP-8 signature");
    Console.WriteLine();
    Console.WriteLine("Sign options:");
    Console.WriteLine("  --skey <file>           Path to payment signing key file (required)");
    Console.WriteLine("  --vkey <file>           Path to payment verification key file (required)");
    Console.WriteLine("  --payload <file>        Path to payload file to sign (required)");
    Console.WriteLine("  --external-aad <text>   Optional external additional authenticated data");
    Console.WriteLine("  --detached              Create detached signature (payload not included)");
    Console.WriteLine("  --hash-payload          Hash the payload before signing");
    Console.WriteLine("  --output <file>         Output file for signature (default: stdout)");
    Console.WriteLine();
    Console.WriteLine("Verify options:");
    Console.WriteLine("  --signature <sig>       CIP-8 signature string or path to file (required)");
    Console.WriteLine("  --vkey <file>           Path to payment verification key file (required)");
    Console.WriteLine("  --payload <file>        Path to payload file (for detached signatures)");
    Console.WriteLine("  --external-aad <text>   External AAD used in signing");
}

static async Task<int> HandleSign(string[] args)
{
    string? skeyPath = null;
    string? vkeyPath = null;
    string? payloadPath = null;
    string? externalAad = null;
    string? outputPath = null;
    bool detached = false;
    bool hashPayload = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--skey":
                if (i + 1 < args.Length)
                {
                    skeyPath = args[++i];
                }

                break;
            case "--vkey":
                if (i + 1 < args.Length)
                {
                    vkeyPath = args[++i];
                }

                break;
            case "--payload":
                if (i + 1 < args.Length)
                {
                    payloadPath = args[++i];
                }

                break;
            case "--external-aad":
                if (i + 1 < args.Length)
                {
                    externalAad = args[++i];
                }

                break;
            case "--output":
                if (i + 1 < args.Length)
                {
                    outputPath = args[++i];
                }

                break;
            case "--detached":
                detached = true;
                break;
            case "--hash-payload":
                hashPayload = true;
                break;
            default:
                break;
        }
    }

    if (skeyPath == null || vkeyPath == null || payloadPath == null)
    {
        Console.WriteLine("Error: Missing required arguments");
        Console.WriteLine("Required: --skey <file> --vkey <file> --payload <file>");
        return 1;
    }

    await SignMessage(skeyPath, vkeyPath, payloadPath, externalAad, detached, hashPayload, outputPath).ConfigureAwait(false);
    return 0;
}

static async Task<int> HandleVerify(string[] args)
{
    string? signatureInput = null;
    string? vkeyPath = null;
    string? payloadPath = null;
    string? externalAad = null;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--signature":
                if (i + 1 < args.Length)
                {
                    signatureInput = args[++i];
                }

                break;
            case "--vkey":
                if (i + 1 < args.Length)
                {
                    vkeyPath = args[++i];
                }

                break;
            case "--payload":
                if (i + 1 < args.Length)
                {
                    payloadPath = args[++i];
                }

                break;
            case "--external-aad":
                if (i + 1 < args.Length)
                {
                    externalAad = args[++i];
                }

                break;
            default:
                break;
        }
    }

    if (signatureInput == null || vkeyPath == null)
    {
        Console.WriteLine("Error: Missing required arguments");
        Console.WriteLine("Required: --signature <sig> --vkey <file>");
        return 1;
    }

    return await VerifySignature(signatureInput, vkeyPath, payloadPath, externalAad).ConfigureAwait(false);
}

static async Task SignMessage(string skeyPath, string vkeyPath, string payloadPath, string? externalAad, bool detached, bool hashPayload, string? outputPath)
{
    // Read the signing key
    string skeyJson = await File.ReadAllTextAsync(skeyPath).ConfigureAwait(false);
    string skeyCborHex = ReadCborHexFromKeyFile(skeyJson)
        ?? throw new InvalidOperationException("Invalid signing key file format");

    // Read the verification key
    string vkeyJson = await File.ReadAllTextAsync(vkeyPath).ConfigureAwait(false);
    string vkeyCborHex = ReadCborHexFromKeyFile(vkeyJson)
        ?? throw new InvalidOperationException("Invalid verification key file format");

    // Parse the CBOR hex to get the actual key bytes
    byte[] skeyCbor = Convert.FromHexString(skeyCborHex);
    byte[] vkeyCbor = Convert.FromHexString(vkeyCborHex);

    // The CBOR should be a byte string containing the key
    CborReader skeyReader = new(skeyCbor);
    byte[] skeyBytes = skeyReader.ReadByteString().ToArray();

    CborReader vkeyReader = new(vkeyCbor);
    byte[] vkeyBytes = vkeyReader.ReadByteString().ToArray();

    Console.WriteLine($"Signing key length: {skeyBytes.Length} bytes");
    Console.WriteLine($"Verification key length: {vkeyBytes.Length} bytes");

    // For Cardano extended keys, we need to handle the format properly
    PrivateKey privateKey;
    PublicKey publicKey;

    if (skeyBytes.Length == 128)
    {
        // Shelley extended signing key (128 bytes):
        // First 64 bytes: Extended Ed25519 private key
        // Next 32 bytes: Chain code
        // Last 32 bytes: Public key
        byte[] key = new byte[64];
        byte[] chainCode = new byte[32];
        Array.Copy(skeyBytes, 0, key, 0, 64);
        Array.Copy(skeyBytes, 64, chainCode, 0, 32); // Chain code is at offset 64
        privateKey = new PrivateKey(key, chainCode);
    }
    else if (skeyBytes.Length == 64)
    {
        // Extended key: first 32 bytes is the key, last 32 is chain code
        byte[] key = new byte[32];
        byte[] chainCode = new byte[32];
        Array.Copy(skeyBytes, 0, key, 0, 32);
        Array.Copy(skeyBytes, 32, chainCode, 0, 32);
        privateKey = new PrivateKey(key, chainCode);
    }
    else if (skeyBytes.Length == 32)
    {
        // Simple key without chain code
        privateKey = new PrivateKey(skeyBytes, new byte[32]);
    }
    else
    {
        throw new InvalidOperationException($"Unexpected signing key size: {skeyBytes.Length} bytes");
    }

    if (vkeyBytes.Length >= 32)
    {
        // Take first 32 bytes as the public key
        byte[] pubKeyBytes = new byte[32];
        Array.Copy(vkeyBytes, 0, pubKeyBytes, 0, 32);
        byte[] chainCode = vkeyBytes.Length >= 64 ? [.. vkeyBytes.Skip(32).Take(32)] : new byte[32];
        publicKey = new PublicKey(pubKeyBytes, chainCode);
    }
    else
    {
        throw new InvalidOperationException($"Unexpected verification key size: {vkeyBytes.Length} bytes");
    }

    // Read the payload
    byte[] payloadBytes = await File.ReadAllBytesAsync(payloadPath).ConfigureAwait(false);
    Console.WriteLine($"Payload size: {payloadBytes.Length} bytes");

    // Derive the address from the public key for address binding
    // For payment keys, we typically create an enterprise address (no staking part)
    Address address = Address.FromPublicKeys(NetworkType.Mainnet, AddressType.EnterprisePayment, publicKey);
    Console.WriteLine($"Address: {address.ToBech32()}");

    // Build the COSE_Sign1 message
    CoseSign1Builder builder = new CoseSign1Builder()
        .WithPayload(payloadBytes)
        .WithAddress(address);

    if (externalAad != null)
    {
        _ = builder.WithExternalAad(Encoding.UTF8.GetBytes(externalAad));
    }

    if (detached)
    {
        _ = builder.WithDetachedPayload();
    }

    if (hashPayload)
    {
        _ = builder.HashPayload();
    }

    CoseSign1 coseSign1 = builder.Build(privateKey);

    // Convert to CIP-8 format
    string cip8Signature = coseSign1.ToCip8Format();

    Console.WriteLine("\n=== Signature Generated ===");
    Console.WriteLine($"CIP-8 Format: {cip8Signature}");

    // Also output the raw CBOR hex for debugging
    string cborHex = Convert.ToHexStringLower(coseSign1.ToCbor());
    Console.WriteLine($"CBOR Hex: {cborHex}");
    Console.WriteLine($"Public Key (hex): {Convert.ToHexStringLower(publicKey.Key)}");

    if (outputPath != null)
    {
        await File.WriteAllTextAsync(outputPath, cip8Signature).ConfigureAwait(false);
        Console.WriteLine($"\nSignature written to: {outputPath}");
    }

    // Verify the signature we just created
    EdDsaCoseSigner signer = new();
    bool verified = signer.VerifyCoseSign1(
        coseSign1,
        publicKey,
        externalAad != null ? Encoding.UTF8.GetBytes(externalAad) : null,
        detached ? payloadBytes : null
    );

    Console.WriteLine($"\nSelf-verification: {(verified ? "PASSED" : "FAILED")}");
}

static async Task<int> VerifySignature(string signatureInput, string vkeyPath, string? payloadPath, string? externalAad)
{
    // Read the verification key
    string vkeyJson = await File.ReadAllTextAsync(vkeyPath).ConfigureAwait(false);
    string vkeyCborHex = ReadCborHexFromKeyFile(vkeyJson)
        ?? throw new InvalidOperationException("Invalid verification key file format");

    byte[] vkeyCbor = Convert.FromHexString(vkeyCborHex);
    CborReader vkeyReader = new(vkeyCbor);
    byte[] vkeyBytes = vkeyReader.ReadByteString().ToArray();

    PublicKey publicKey;
    if (vkeyBytes.Length >= 32)
    {
        byte[] pubKeyBytes = new byte[32];
        Array.Copy(vkeyBytes, 0, pubKeyBytes, 0, 32);
        byte[] chainCode = vkeyBytes.Length >= 64 ? [.. vkeyBytes.Skip(32).Take(32)] : new byte[32];
        publicKey = new PublicKey(pubKeyBytes, chainCode);
    }
    else
    {
        throw new InvalidOperationException($"Unexpected verification key size: {vkeyBytes.Length} bytes");
    }

    // Read the signature (could be from file or direct input)
    string cip8Signature = File.Exists(signatureInput)
        ? await File.ReadAllTextAsync(signatureInput).ConfigureAwait(false)
        : signatureInput;

    // Parse the CIP-8 signature
    CoseSign1 coseSign1 = CoseMessageExtensions.FromCip8Format(cip8Signature.Trim());

    // Read payload if provided (for detached signatures)
    byte[]? payloadBytes = null;
    if (payloadPath != null)
    {
        payloadBytes = await File.ReadAllBytesAsync(payloadPath).ConfigureAwait(false);
    }

    // Verify the signature
    EdDsaCoseSigner signer = new();
    bool verified = signer.VerifyCoseSign1(
        coseSign1,
        publicKey,
        externalAad != null ? Encoding.UTF8.GetBytes(externalAad) : null,
        payloadBytes
    );

    Console.WriteLine("\n=== Verification Result ===");
    Console.WriteLine($"Status: {(verified ? "VALID" : "INVALID")}");

    if (!verified)
    {
        Console.WriteLine("\nPossible reasons for verification failure:");
        Console.WriteLine("- Wrong verification key");
        Console.WriteLine("- Missing or incorrect external AAD");
        Console.WriteLine("- Missing payload for detached signature");
        Console.WriteLine("- Signature has been tampered with");
    }

    return verified ? 0 : 1;
}

static string? ReadCborHexFromKeyFile(string json)
{
    using JsonDocument doc = JsonDocument.Parse(json);
    return doc.RootElement.TryGetProperty("cborHex", out JsonElement cborHexElement)
        ? cborHexElement.GetString()
        : null;
}

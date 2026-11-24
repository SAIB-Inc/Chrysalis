using System.Text.Json;
using Chrysalis.Crypto;
using Chrysalis.Wallet.Models.Keys;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Wallet.Cli;

internal static class KeyFileReader
{
    private const int ChainCodeSize = 32;
    private const int ExtendedVerificationKeySize = 64;
    private const int ShelleyExtendedSigningKeySize = 128;
    private const int ShelleyExtendedChainCodeOffset = 96;

    internal static PrivateKey ReadSigningKey(string json)
    {
        (string? type, byte[] keyBytes) = ReadEnvelope(json, "Invalid signing key file format");
        if (keyBytes.Length == Ed25519.ExpandedPrivateKeySizeInBytes)
        {
            throw new InvalidOperationException(
                "Unsupported 64-byte signing key envelope (such as VrfSigningKey_PraosVRF); use a 32-byte Ed25519 or 128-byte Shelley extended signing key envelope.");
        }
        if (keyBytes.Length != Ed25519.PrivateKeySeedSizeInBytes && keyBytes.Length != ShelleyExtendedSigningKeySize)
        {
            throw new InvalidOperationException($"Unexpected signing key size: {keyBytes.Length} bytes");
        }
        ValidateKeyType(type, isSigningKey: true);
        return keyBytes.Length == ShelleyExtendedSigningKeySize
            ? new PrivateKey(keyBytes[..Ed25519.ExpandedPrivateKeySizeInBytes], keyBytes[ShelleyExtendedChainCodeOffset..])
            : new PrivateKey(keyBytes, new byte[ChainCodeSize]);
    }

    internal static PublicKey ReadVerificationKey(string json)
    {
        (string? type, byte[] keyBytes) = ReadEnvelope(json, "Invalid verification key file format");
        if (keyBytes.Length != Ed25519.PublicKeySizeInBytes && keyBytes.Length != ExtendedVerificationKeySize)
        {
            throw new InvalidOperationException($"Unexpected verification key size: {keyBytes.Length} bytes");
        }
        ValidateKeyType(type, isSigningKey: false);
        return keyBytes.Length == ExtendedVerificationKeySize
            ? new PublicKey(keyBytes[..Ed25519.PublicKeySizeInBytes], keyBytes[Ed25519.PublicKeySizeInBytes..])
            : new PublicKey(keyBytes, new byte[ChainCodeSize]);
    }

    private static (string? Type, byte[] KeyBytes) ReadEnvelope(string json, string missingCborMessage)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("cborHex", out JsonElement cborHexElement) ||
            cborHexElement.ValueKind != JsonValueKind.String ||
            cborHexElement.GetString() is not string cborHex ||
            string.IsNullOrWhiteSpace(cborHex))
        {
            throw new InvalidOperationException(missingCborMessage);
        }

        string? type = null;
        if (doc.RootElement.TryGetProperty("type", out JsonElement typeElement))
        {
            if (typeElement.ValueKind != JsonValueKind.String ||
                typeElement.GetString() is not string rawType ||
                string.IsNullOrWhiteSpace(rawType))
            {
                throw new InvalidOperationException("Invalid key envelope 'type' field.");
            }
            type = rawType;
        }

        CborReader reader = new(Convert.FromHexString(cborHex));
        byte[] keyBytes = reader.ReadByteString().ToArray();
        if (!reader.Buffer.IsEmpty)
        {
            throw new InvalidOperationException("Unexpected trailing CBOR bytes in key envelope.");
        }
        return (type, keyBytes);
    }

    private static void ValidateKeyType(string? type, bool isSigningKey)
    {
        if (type is null)
        {
            return;
        }
        string requiredRole = isSigningKey ? "SigningKey" : "VerificationKey";
        string forbiddenRole = isSigningKey ? "VerificationKey" : "SigningKey";
        bool hasEd25519 = type.EndsWith("_ed25519", StringComparison.Ordinal) || type.EndsWith("_ed25519_bip32", StringComparison.Ordinal);
        if (!hasEd25519 || !type.Contains(requiredRole, StringComparison.Ordinal) || type.Contains(forbiddenRole, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported or mismatched {(isSigningKey ? "signing" : "verification")} key envelope type '{type}'.");
        }
    }
}

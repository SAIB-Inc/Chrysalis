using System.Text;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Wallet.CIPs.CIP8.Models;

namespace Chrysalis.Wallet.CIPs.CIP8.Extensions;

/// <summary>
/// Extension methods for COSE message handling and CIP-8 format conversion
/// </summary>
public static class CoseMessageExtensions
{
    private const int ChecksumByteLength = sizeof(uint);
    private static readonly int EncodedChecksumLength = Base64UrlEncode(new byte[ChecksumByteLength]).Length;

    /// <summary>
    /// Converts a COSE message to CIP-8 format with prefix and checksum
    /// </summary>
    public static string ToCip8Format(this ICoseMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        
        var cbor = message.ToCbor();
        var base64url = Base64UrlEncode(cbor);
        
        // Add appropriate prefix based on message type
        var prefix = message switch
        {
            CoseSign1 => "cms_",     // COSE Message Signature1
            _ => throw new NotSupportedException($"Message type {message.GetType().Name} is not supported for CIP-8 format")
        };
        
        // Calculate FNV32a checksum
        var dataToChecksum = $"{prefix}{base64url}";
        var checksum = CalculateFnv32a(dataToChecksum);
        var checksumBase64 = Base64UrlEncode(checksum);
        
        return $"{prefix}{base64url}_{checksumBase64}";
    }
    
    /// <summary>
    /// Parses a CIP-8 formatted message
    /// </summary>
    public static CoseSign1 FromCip8Format(string cip8Message)
    {
        ArgumentNullException.ThrowIfNull(cip8Message);
        
        // Validate format
        // Find the first underscore (after prefix)
        var firstUnderscore = cip8Message.IndexOf('_');
        if (firstUnderscore == -1)
            throw new FormatException("Invalid CIP-8 format. Expected format: prefix_data_checksum");

        var prefix = cip8Message.Substring(0, firstUnderscore + 1);

        // We always append "_" + checksum where the checksum is the Base64Url text of a 4-byte hash.
        if (cip8Message.Length <= firstUnderscore + 1 + EncodedChecksumLength)
            throw new FormatException("Invalid CIP-8 format. Missing payload or checksum");

        var separatorIndex = cip8Message.Length - EncodedChecksumLength - 1;
        if (separatorIndex <= firstUnderscore || cip8Message[separatorIndex] != '_')
            throw new FormatException("Invalid CIP-8 format. Could not isolate checksum");

        var data = cip8Message.Substring(firstUnderscore + 1, separatorIndex - firstUnderscore - 1);
        var checksum = cip8Message.Substring(separatorIndex + 1);

        if (checksum.Length != EncodedChecksumLength)
            throw new FormatException("Invalid CIP-8 format. Invalid checksum length");
        if (string.IsNullOrEmpty(data))
            throw new FormatException("Invalid CIP-8 format. Missing payload data");

        // Verify checksum
        var dataToChecksum = $"{prefix}{data}";
        var expectedChecksum = CalculateFnv32a(dataToChecksum);
        var actualChecksum = Base64UrlDecode(checksum);
        
        if (!expectedChecksum.SequenceEqual(actualChecksum))
            throw new InvalidOperationException("Invalid checksum in CIP-8 message");
        
        // Decode based on prefix
        var cbor = Base64UrlDecode(data);
        return prefix switch
        {
            "cms_" => CborSerializer.Deserialize<CoseSign1>(cbor),
            _ => throw new NotSupportedException($"Unknown CIP-8 prefix: {prefix}")
        };
    }
    
    /// <summary>
    /// Reconstructs the SigStructure for verification
    /// </summary>
    public static SigStructure GetSigStructure(
        this CoseSign1 message, 
        byte[]? externalAad = null,
        byte[]? payload = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        
        var actualPayload = payload ?? message.Payload;
        if (actualPayload == null)
            throw new ArgumentException("Payload is required. Either the message must contain it or it must be provided as a parameter.");
        
        return new SigStructure(
            Context: SigContext.Signature1,
            BodyProtected: message.ProtectedHeaders,
            SignProtected: [],
            ExternalAad: externalAad ?? [],
            Payload: actualPayload
        );
    }
    
    /// <summary>
    /// Base64 URL encoding without padding
    /// </summary>
    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
    
    /// <summary>
    /// Base64 URL decoding
    /// </summary>
    private static byte[] Base64UrlDecode(string base64url)
    {
        var base64 = base64url
            .Replace('-', '+')
            .Replace('_', '/');
            
        // Add padding if needed
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        
        return Convert.FromBase64String(base64);
    }
    
    /// <summary>
    /// Calculates FNV-1a 32-bit hash
    /// </summary>
    private static byte[] CalculateFnv32a(string data)
    {
        const uint FNV_PRIME = 0x01000193;
        const uint FNV_OFFSET_BASIS = 0x811c9dc5;
        
        uint hash = FNV_OFFSET_BASIS;
        var bytes = Encoding.UTF8.GetBytes(data);
        
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= FNV_PRIME;
        }
        
        return BitConverter.GetBytes(hash);
    }
}

using System.Formats.Cbor;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// Extended header map that supports both standard and custom headers
/// For CIP-8, this is typically just used to hold "address" and "hashed" headers
/// </summary>
public class ExtendedHeaderMap
{
    private readonly Dictionary<string, byte[]> _headers;
    
    public ExtendedHeaderMap()
    {
        _headers = new Dictionary<string, byte[]>();
    }
    
    /// <summary>
    /// Sets a header value
    /// </summary>
    public void SetHeader(string label, byte[] value)
    {
        _headers[label] = value;
    }
    
    /// <summary>
    /// Gets a header value
    /// </summary>
    public byte[]? GetHeader(string label)
    {
        return _headers.TryGetValue(label, out var value) ? value : null;
    }
    
    /// <summary>
    /// Checks if the header map is empty
    /// </summary>
    public bool IsEmpty() => _headers.Count == 0;
    
    /// <summary>
    /// Converts to CBOR bytes as a map
    /// </summary>
    public byte[] ToCbor()
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        
        writer.WriteStartMap(_headers.Count);
        foreach (var kvp in _headers)
        {
            writer.WriteTextString(kvp.Key);
            writer.WriteByteString(kvp.Value);
        }
        writer.WriteEndMap();
        
        return writer.Encode();
    }
    
    /// <summary>
    /// Creates a copy with an additional header
    /// </summary>
    public ExtendedHeaderMap WithHeader(string label, byte[] value)
    {
        var newMap = new ExtendedHeaderMap();
        foreach (var kvp in _headers)
        {
            newMap._headers[kvp.Key] = kvp.Value;
        }
        newMap._headers[label] = value;
        return newMap;
    }
}
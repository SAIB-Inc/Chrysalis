using System;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// Protected headers that are serialized as a byte string
/// </summary>
public class ProtectedHeaderMap
{
    private readonly byte[] _serializedMap;
    
    /// <summary>
    /// Creates a new ProtectedHeaderMap from a HeaderMap
    /// </summary>
    public ProtectedHeaderMap(HeaderMap headerMap)
    {
        if (headerMap == null)
            throw new ArgumentNullException(nameof(headerMap));
            
        // COSE spec: empty protected headers should be encoded as zero-length byte string
        _serializedMap = headerMap.IsEmpty() 
            ? []
            : CborSerializer.Serialize(headerMap);
    }
    
    /// <summary>
    /// Creates a ProtectedHeaderMap from already serialized bytes
    /// </summary>
    public ProtectedHeaderMap(byte[] serializedMap)
    {
        _serializedMap = serializedMap ?? throw new ArgumentNullException(nameof(serializedMap));
    }
    
    /// <summary>
    /// Gets the serialized bytes of the protected headers
    /// </summary>
    public byte[] GetBytes() => _serializedMap;
    
    /// <summary>
    /// Deserializes the protected headers back to a HeaderMap
    /// </summary>
    public HeaderMap Deserialize()
    {
        if (_serializedMap.Length == 0)
            return HeaderMap.Empty;
            
        return CborSerializer.Deserialize<HeaderMap>(_serializedMap);
    }
}
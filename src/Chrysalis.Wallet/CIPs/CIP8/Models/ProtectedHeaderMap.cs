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
    /// Creates a protected header map from serialized CBOR bytes
    /// </summary>
    public ProtectedHeaderMap(byte[] serializedMap)
    {
        _serializedMap = serializedMap ?? Array.Empty<byte>();
    }
    
    /// <summary>
    /// Creates a protected header map from a HeaderMap
    /// </summary>
    public ProtectedHeaderMap(HeaderMap headerMap)
    {
        if (headerMap == null || headerMap == HeaderMap.Empty)
        {
            _serializedMap = Array.Empty<byte>();
        }
        else
        {
            _serializedMap = CborSerializer.Serialize(headerMap);
        }
    }
    
    /// <summary>
    /// Gets the serialized CBOR bytes
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
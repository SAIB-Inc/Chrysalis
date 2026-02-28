using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// Protected headers that are serialized as a byte string.
/// </summary>
public class ProtectedHeaderMap
{
    private readonly byte[] _serializedMap;

    /// <summary>
    /// Creates a protected header map from serialized CBOR bytes.
    /// </summary>
    /// <param name="serializedMap">The serialized CBOR bytes.</param>
    public ProtectedHeaderMap(byte[] serializedMap)
    {
        _serializedMap = serializedMap ?? [];
    }

    /// <summary>
    /// Creates a protected header map from a HeaderMap.
    /// </summary>
    /// <param name="headerMap">The header map to serialize.</param>
    public ProtectedHeaderMap(HeaderMap headerMap)
    {
        _serializedMap = headerMap == null || headerMap == HeaderMap.Empty
            ? []
            : CborSerializer.Serialize(headerMap);
    }

    /// <summary>
    /// Gets the serialized CBOR bytes.
    /// </summary>
    /// <returns>The serialized byte array.</returns>
    public byte[] GetBytes()
    {
        return _serializedMap;
    }

    /// <summary>
    /// Deserializes the protected headers back to a HeaderMap.
    /// </summary>
    /// <returns>The deserialized HeaderMap, or an empty HeaderMap if no data.</returns>
    public HeaderMap Deserialize()
    {
        return _serializedMap.Length == 0 ? HeaderMap.Empty : CborSerializer.Deserialize<HeaderMap>(_serializedMap);
    }
}

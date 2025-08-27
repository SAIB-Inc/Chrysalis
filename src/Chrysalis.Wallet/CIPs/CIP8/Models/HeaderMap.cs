using Chrysalis.Cbor.Types;
using System.Formats.Cbor;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE header map containing standard and custom headers
/// </summary>
public record HeaderMap : CborBase
{
    private readonly Dictionary<object, object> _headers;
    
    public HeaderMap()
    {
        _headers = new Dictionary<object, object>();
    }
    
    public HeaderMap(Dictionary<object, object> headers)
    {
        _headers = headers ?? new Dictionary<object, object>();
    }
    
    /// <summary>
    /// Creates an empty header map
    /// </summary>
    public static HeaderMap Empty { get; } = new();
    
    /// <summary>
    /// Creates a header map with the "hashed" field
    /// </summary>
    public static HeaderMap WithHashed(bool hashed)
    {
        var headers = new Dictionary<object, object>
        {
            ["hashed"] = hashed
        };
        return new HeaderMap(headers);
    }
    
    /// <summary>
    /// Checks if the header map is empty
    /// </summary>
    public bool IsEmpty() => _headers.Count == 0;
    
    /// <summary>
    /// Serializes the header map to CBOR
    /// </summary>
    public byte[] ToCbor()
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        Write(writer, this);
        return writer.Encode();
    }
    
    public static void Write(CborWriter writer, HeaderMap data)
    {
        writer.WriteStartMap(data._headers.Count);
        
        foreach (var kvp in data._headers)
        {
            // Write key
            if (kvp.Key is string strKey)
                writer.WriteTextString(strKey);
            else if (kvp.Key is int intKey)
                writer.WriteInt32(intKey);
            else if (kvp.Key is long longKey)
                writer.WriteInt64(longKey);
            
            // Write value
            if (kvp.Value is bool boolVal)
                writer.WriteBoolean(boolVal);
            else if (kvp.Value is byte[] bytesVal)
                writer.WriteByteString(bytesVal);
            else if (kvp.Value is string strVal)
                writer.WriteTextString(strVal);
            else if (kvp.Value is int intVal)
                writer.WriteInt32(intVal);
        }
        
        writer.WriteEndMap();
    }
    
    public static new HeaderMap Read(ReadOnlyMemory<byte> data)
    {
        var reader = new CborReader(data, CborConformanceMode.Lax);
        var headers = new Dictionary<object, object>();
        
        if (reader.PeekState() == CborReaderState.StartMap)
        {
            var count = reader.ReadStartMap();
            
            for (int i = 0; i < (count ?? int.MaxValue) && reader.PeekState() != CborReaderState.EndMap; i++)
            {
                // Read key
                object key = reader.PeekState() switch
                {
                    CborReaderState.TextString => reader.ReadTextString(),
                    CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => reader.ReadInt32(),
                    _ => reader.ReadInt32()
                };
                
                // Read value
                object value = reader.PeekState() switch
                {
                    CborReaderState.Boolean => reader.ReadBoolean(),
                    CborReaderState.ByteString => reader.ReadByteString(),
                    CborReaderState.TextString => reader.ReadTextString(),
                    CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => reader.ReadInt32(),
                    _ => reader.ReadByteString()
                };
                
                headers[key] = value;
            }
            
            reader.ReadEndMap();
        }
        
        return new HeaderMap(headers);
    }
}
using System.Buffers;
using Dahomey.Cbor.Serialization;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// Extended header map that supports both standard and custom headers.
/// </summary>
public class ExtendedHeaderMap
{
    private readonly Dictionary<string, byte[]> _headers;

    /// <summary>Initializes a new empty ExtendedHeaderMap.</summary>
    public ExtendedHeaderMap()
    {
        _headers = [];
    }

    /// <summary>Sets a header value by label.</summary>
    public void SetHeader(string label, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);
        _headers[label] = value;
    }

    /// <summary>Gets a header value by label, or null if not found.</summary>
    public byte[]? GetHeader(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return _headers.TryGetValue(label, out byte[]? value) ? value : null;
    }

    /// <summary>Checks if the header map is empty.</summary>
    public bool IsEmpty()
    {
        return _headers.Count == 0;
    }

    /// <summary>Converts to CBOR bytes as a map.</summary>
    public byte[] ToCbor()
    {
        ArrayBufferWriter<byte> output = new();
        CborWriter writer = new(output);

        writer.WriteBeginMap(_headers.Count);
        foreach (KeyValuePair<string, byte[]> kvp in _headers)
        {
            writer.WriteString(kvp.Key);
            writer.WriteByteString(kvp.Value);
        }
        writer.WriteEndMap(_headers.Count);

        return output.WrittenSpan.ToArray();
    }

    /// <summary>Creates a copy with an additional header.</summary>
    public ExtendedHeaderMap WithHeader(string label, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);

        ExtendedHeaderMap newMap = new();
        foreach (KeyValuePair<string, byte[]> kvp in _headers)
        {
            newMap._headers[kvp.Key] = kvp.Value;
        }
        newMap._headers[label] = value;
        return newMap;
    }
}

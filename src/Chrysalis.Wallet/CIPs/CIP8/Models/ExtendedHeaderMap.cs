using System.Formats.Cbor;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// Extended header map that supports both standard and custom headers.
/// For CIP-8, this is typically just used to hold "address" and "hashed" headers.
/// </summary>
public class ExtendedHeaderMap
{
    private readonly Dictionary<string, byte[]> _headers;

    /// <summary>
    /// Initializes a new empty ExtendedHeaderMap.
    /// </summary>
    public ExtendedHeaderMap()
    {
        _headers = [];
    }

    /// <summary>
    /// Sets a header value.
    /// </summary>
    /// <param name="label">The header label.</param>
    /// <param name="value">The header value as bytes.</param>
    public void SetHeader(string label, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);

        _headers[label] = value;
    }

    /// <summary>
    /// Gets a header value.
    /// </summary>
    /// <param name="label">The header label.</param>
    /// <returns>The header value as bytes, or null if not found.</returns>
    public byte[]? GetHeader(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        return _headers.TryGetValue(label, out byte[]? value) ? value : null;
    }

    /// <summary>
    /// Checks if the header map is empty.
    /// </summary>
    /// <returns>True if the header map contains no entries.</returns>
    public bool IsEmpty()
    {
        return _headers.Count == 0;
    }

    /// <summary>
    /// Converts to CBOR bytes as a map.
    /// </summary>
    /// <returns>The CBOR-encoded byte representation.</returns>
    public byte[] ToCbor()
    {
        CborWriter writer = new(CborConformanceMode.Lax);

        writer.WriteStartMap(_headers.Count);
        foreach (KeyValuePair<string, byte[]> kvp in _headers)
        {
            writer.WriteTextString(kvp.Key);
            writer.WriteByteString(kvp.Value);
        }
        writer.WriteEndMap();

        return writer.Encode();
    }

    /// <summary>
    /// Creates a copy with an additional header.
    /// </summary>
    /// <param name="label">The header label.</param>
    /// <param name="value">The header value as bytes.</param>
    /// <returns>A new ExtendedHeaderMap with the additional header.</returns>
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

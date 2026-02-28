using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE header map containing standard and custom headers
/// Uses CborLabel for type-safe header keys (int or string)
/// Uses CborPrimitive for type-safe header values (int, string, bool, bytes, etc.)
/// </summary>
[CborSerializable]
public partial record HeaderMap(
    Dictionary<CborLabel, CborPrimitive> Headers
) : CborBase
{
    /// <summary>
    /// Creates an empty header map
    /// </summary>
    public static HeaderMap Empty { get; } = new([]);

    /// <summary>
    /// Creates a header map with the "hashed" field
    /// </summary>
    public static HeaderMap WithHashed(bool hashed)
    {
        Dictionary<CborLabel, CborPrimitive> headers = new()
        {
            ["hashed"] = hashed
        };
        return new(headers);
    }

    /// <summary>
    /// Adds a header with an integer label
    /// </summary>
    public HeaderMap WithHeader(int label, CborPrimitive value)
    {
        Dictionary<CborLabel, CborPrimitive> newHeaders = new(Headers)
        {
            [label] = value
        };
        return new(newHeaders);
    }

    /// <summary>
    /// Adds a header with a string label
    /// </summary>
    public HeaderMap WithHeader(string label, CborPrimitive value)
    {
        Dictionary<CborLabel, CborPrimitive> newHeaders = new(Headers)
        {
            [label] = value
        };
        return new(newHeaders);
    }
}
using System.Collections.Generic;
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
    public static HeaderMap Empty { get; } = new(new Dictionary<CborLabel, CborPrimitive>());

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
    /// Checks if the header map is empty
    /// </summary>
    public bool IsEmpty() => Headers.Count == 0;

    /// <summary>
    /// Adds a header with integer label (standard COSE headers)
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
    /// Adds a header with string label (custom headers)
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
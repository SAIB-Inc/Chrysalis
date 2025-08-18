using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE header map containing standard and custom headers
/// </summary>
[CborSerializable]
public partial record HeaderMap(Dictionary<CborLabel, CborPrimitive> Headers) : CborBase
{
    public HeaderMap() : this(new Dictionary<CborLabel, CborPrimitive>()) { }
    
    /// <summary>
    /// Creates an empty header map
    /// </summary>
    public static HeaderMap Empty { get; } = new();
    
    /// <summary>
    /// Creates a header map with the "hashed" field
    /// </summary>
    public static HeaderMap WithHashed(bool hashed) => new(new Dictionary<CborLabel, CborPrimitive>
    {
        [new CborLabel("hashed")] = new CborBool(hashed)
    });
    
    /// <summary>
    /// Checks if the header map is empty
    /// </summary>
    public bool IsEmpty() => Headers.Count == 0;
    
    /// <summary>
    /// Serializes the header map to CBOR
    /// </summary>
    public byte[] ToCbor() => CborSerializer.Serialize(this);
}
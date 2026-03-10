using System.Buffers;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE header map containing standard and custom headers.
/// Uses CborLabel for type-safe header keys (int or string).
/// Uses CborEncodedValue for opaque CBOR-encoded header values.
/// </summary>
[CborSerializable]
public partial record HeaderMap(
    Dictionary<CborLabel, CborEncodedValue> Headers
) : CborRecord
{
    /// <summary>
    /// Creates an empty header map.
    /// </summary>
    public static HeaderMap Empty { get; } = new([]);

    /// <summary>
    /// Creates a header map with the "hashed" field.
    /// </summary>
    public static HeaderMap WithHashed(bool hashed)
    {
        Dictionary<CborLabel, CborEncodedValue> headers = new()
        {
            ["hashed"] = EncodePrimitive(writer => writer.WriteBoolean(hashed))
        };
        return new(headers);
    }

    /// <summary>
    /// Adds a header with an integer label and an integer value.
    /// </summary>
    public HeaderMap WithHeader(int label, int value)
    {
        Dictionary<CborLabel, CborEncodedValue> newHeaders = new(Headers)
        {
            [label] = EncodePrimitive(writer => writer.WriteInt32(value))
        };
        return new(newHeaders);
    }

    /// <summary>
    /// Adds a header with an integer label and a string value.
    /// </summary>
    public HeaderMap WithHeader(int label, string value)
    {
        Dictionary<CborLabel, CborEncodedValue> newHeaders = new(Headers)
        {
            [label] = EncodePrimitive(writer => writer.WriteString(value))
        };
        return new(newHeaders);
    }

    /// <summary>
    /// Adds a header with a string label and a string value.
    /// </summary>
    public HeaderMap WithHeader(string label, string value)
    {
        Dictionary<CborLabel, CborEncodedValue> newHeaders = new(Headers)
        {
            [label] = EncodePrimitive(writer => writer.WriteString(value))
        };
        return new(newHeaders);
    }

    /// <summary>
    /// Adds a header with a string label and a boolean value.
    /// </summary>
    public HeaderMap WithHeader(string label, bool value)
    {
        Dictionary<CborLabel, CborEncodedValue> newHeaders = new(Headers)
        {
            [label] = EncodePrimitive(writer => writer.WriteBoolean(value))
        };
        return new(newHeaders);
    }

    /// <summary>
    /// Adds a header with an integer label and a raw CborEncodedValue.
    /// </summary>
    public HeaderMap WithHeader(int label, CborEncodedValue value)
    {
        Dictionary<CborLabel, CborEncodedValue> newHeaders = new(Headers)
        {
            [label] = value
        };
        return new(newHeaders);
    }

    /// <summary>
    /// Adds a header with a string label and a raw CborEncodedValue.
    /// </summary>
    public HeaderMap WithHeader(string label, CborEncodedValue value)
    {
        Dictionary<CborLabel, CborEncodedValue> newHeaders = new(Headers)
        {
            [label] = value
        };
        return new(newHeaders);
    }

    /// <summary>
    /// Helper to encode a primitive value as a CborEncodedValue.
    /// </summary>
    private static CborEncodedValue EncodePrimitive(Action<CborWriter> writeAction)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer);
        writeAction(writer);
        return new CborEncodedValue(buffer.WrittenMemory);
    }
}

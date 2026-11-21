using System.Buffers;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Multiplexer;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

/// <summary>Represents a global (era-independent) query in the LocalStateQuery mini-protocol.</summary>
[CborSerializable]
public partial record GlobalQuery : CborRecord, IQueryReq
{
    private readonly ReadOnlyMemory<byte> _arguments;
    private readonly int _argumentCount;

    /// <summary>The numeric identifier of the global query type.</summary>
    public ulong Query { get; init; }

    /// <summary>Initializes a new <see cref="GlobalQuery"/> with the specified query selector.</summary>
    public GlobalQuery(ulong Query) => this.Query = Query;

    internal GlobalQuery(ulong query, ReadOnlyMemory<byte> raw, ReadOnlyMemory<byte> arguments, int argumentCount, bool isIndefinite) =>
        (Query, Raw, _arguments, _argumentCount, IsIndefinite) = (query, raw, arguments, argumentCount, isIndefinite);

    /// <summary>Deconstructs this query into its numeric selector.</summary>
    public void Deconstruct(out ulong Query) => Query = this.Query;

    internal static GlobalQuery FromPrefix(ulong query, ReadOnlyMemory<byte> data, int prefix, bool isIndefinite, out int bytesConsumed)
    {
        bytesConsumed = prefix + (isIndefinite && prefix < data.Length && data.Span[prefix] == 0xFF ? 1 : 0);
        return new(query, data[..bytesConsumed], default, 0, isIndefinite);
    }

    /// <summary>Deserializes a <see cref="GlobalQuery"/> instance from CBOR-encoded bytes and reports bytes consumed.</summary>
    public static GlobalQuery Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        ReadOnlySpan<byte> rest = data.Span;
        if (!QueryUnionReader.TryArray(ref rest, out int count) || count == 0)
        {
            throw CborError.Decode(data, 0, nameof(GlobalQuery), "global query array");
        }
        int selectorOffset = data.Length - rest.Length;
        if (!NetworkUnionPrefix.TrySkipTag(ref rest) || !NetworkUnionPrefix.TryHead(ref rest, out byte initial, out ulong query) || initial >> 5 != 0)
        {
            throw CborError.Decode(data, selectorOffset, nameof(GlobalQuery), "unsigned query selector");
        }
        int prefix = data.Length - rest.Length;
        if (count > 1 && CborFrameScanner.GetFrameLength(data.Span) is > 0 and int frameLength)
        {
            bytesConsumed = frameLength;
            return new(query, data[..frameLength], data[prefix..frameLength], count - 1, false);
        }
        return FromPrefix(query, data, prefix, count < 0, out bytesConsumed);
    }

    /// <summary>Deserializes a <see cref="GlobalQuery"/> instance from CBOR-encoded bytes.</summary>
    public static GlobalQuery Read(ReadOnlyMemory<byte> data) => Read(data, out _);

    /// <summary>Serializes a <see cref="GlobalQuery"/> instance to CBOR format.</summary>
    public static void Write(IBufferWriter<byte> output, GlobalQuery data)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Raw.Length > 0) { output.Write(data.Raw.Span); return; }
        int arraySize = data.IsIndefinite ? -1 : 1 + data._argumentCount;
        CborWriter writer = new(output);
        writer.WriteBeginArray(arraySize);
        writer.WriteUInt64(data.Query);
        if (!data._arguments.IsEmpty) { output.Write(data._arguments.Span); }
        writer.WriteEndArray(arraySize);
    }

    /// <summary>Creates a new <see cref="GlobalQuery"/> instance from values.</summary>
    public static GlobalQuery Create(ulong query)
    {
        ArrayBufferWriter<byte> buffer = new();
        Write(buffer, new GlobalQuery(query));
        return Read((ReadOnlyMemory<byte>)buffer.WrittenMemory.ToArray());
    }
}

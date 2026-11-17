using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Network.Core;
using Xunit;

namespace Chrysalis.Network.Tests.Receive;

internal enum ReceiveConsumer { Buffer, Read, Poll, Wait, All }
internal enum ReceiveAction { Deliver, Call, Cancel, End, Dispose }
internal sealed record ReceiveStep(ReceiveAction Action, ProtocolType Protocol = default, int Bytes = 0);
internal sealed record ReceiveSegment(int Index, ProtocolType Protocol, byte[] Payload, int WireEnd);
internal sealed record ReceiveMessage(ProtocolType Protocol, byte[] Bytes, int LastSegment);

// The manifest is built from the generator's cuts, never by asking either scanner or assembler.
internal sealed class ReceiveSchedule(string name)
{
    internal string Name { get; } = name;
    internal Dictionary<ProtocolType, ReceiveConsumer> Consumers { get; } = [];
    internal List<ReceiveSegment> Segments { get; } = [];
    internal List<ReceiveMessage> Messages { get; } = [];
    internal List<ReceiveStep> Steps { get; } = [];
    internal List<byte> Wire { get; } = [];
    internal string? DeclaredDifference { get; set; }

    internal void Stream(ProtocolType protocol, IReadOnlyList<byte[]> messages, params int[] cuts)
    {
        byte[] bytes = [.. messages.SelectMany(message => message)];
        int offset = 0;
        int messageEnd = 0;
        int messageIndex = 0;
        foreach (int size in cuts)
        {
            Assert.InRange(size, 0, ushort.MaxValue);
            Segment(protocol, bytes.AsSpan(offset, size).ToArray());
            offset += size;
            while (messageIndex < messages.Count && messageEnd + messages[messageIndex].Length <= offset)
            {
                byte[] message = messages[messageIndex++];
                messageEnd += message.Length;
                Messages.Add(new(protocol, message, Segments.Count - 1));
            }
        }
        Assert.Equal(bytes.Length, offset);
        Assert.Equal(messages.Count, messageIndex);
    }

    internal void Segment(ProtocolType protocol, byte[] payload)
    {
        byte[] frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)Segments.Count);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)protocol);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6), (ushort)payload.Length);
        payload.CopyTo(frame, 8);
        Wire.AddRange(frame);
        Segments.Add(new(Segments.Count, protocol, payload, Wire.Count));
    }

    internal void Interleave(ScheduleRandom random)
    {
        Dictionary<ProtocolType, Queue<ReceiveSegment>> streams = Segments.GroupBy(segment => segment.Protocol)
            .ToDictionary(group => group.Key, group => new Queue<ReceiveSegment>(group));
        Dictionary<int, int> remap = [];
        Segments.Clear();
        Wire.Clear();
        while (streams.Count != 0)
        {
            ProtocolType protocol = streams.Keys.ElementAt(random.Next(streams.Count));
            ReceiveSegment segment = streams[protocol].Dequeue();
            remap.Add(segment.Index, Segments.Count);
            Segment(protocol, segment.Payload);
            if (streams[protocol].Count == 0) { _ = streams.Remove(protocol); }
        }
        for (int i = 0; i < Messages.Count; i++) { Messages[i] = Messages[i] with { LastSegment = remap[Messages[i].LastSegment] }; }
    }

    internal void Call(ProtocolType protocol, int count = 1)
    {
        for (int i = 0; i < count; i++) { Steps.Add(new(ReceiveAction.Call, protocol)); }
    }

    internal void DeliverAll() => Steps.Add(new(ReceiveAction.Deliver, Bytes: Wire.Count));

    internal byte[] ItemBytes(int segmentIndex, bool wholeMessages) => wholeMessages
        ? [.. Messages.Where(message => message.LastSegment == segmentIndex).SelectMany(message => message.Bytes)]
        : Segments[segmentIndex].Payload;

    internal string Manifest() => JsonSerializer.Serialize(new
    {
        Name,
        DeclaredDifference,
        Consumers,
        Segments = Segments.Select(segment => new { segment.Index, segment.Protocol, Bytes = segment.Payload.Length, Sha256 = Hash(segment.Payload), segment.WireEnd }),
        Messages = Messages.Select(message => new { message.Protocol, Bytes = message.Bytes.Length, Sha256 = Hash(message.Bytes), message.LastSegment }),
        Steps
    });

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

internal static class ReceiveVectors
{
    private static readonly Dictionary<int, byte[]> Cache = Load();
    internal static byte[] Body(int size = 1) => Cache[size];

    private static Dictionary<int, byte[]> Load()
    {
        static byte[] Read(string file)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Chrysalis.Network.Tests.Fixtures.Receive." + file)
                ?? throw new InvalidOperationException("Missing independent receive vector: " + file);
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        using JsonDocument manifest = JsonDocument.Parse(Read("manifest.json"));
        return manifest.RootElement.GetProperty("vectors").EnumerateArray().ToDictionary(entry =>
                entry.GetProperty("payloadBytes").GetInt32(),
            entry =>
            {
                byte[] bytes;
                if (entry.TryGetProperty("file", out JsonElement file)) { bytes = Read(file.GetString()!); }
                else
                {
                    byte[] header = Convert.FromHexString(entry.GetProperty("headerHex").GetString()!);
                    JsonElement fill = entry.GetProperty("fill");
                    Assert.Equal("index-modulo", fill.GetProperty("kind").GetString());
                    int payloadLength = entry.GetProperty("payloadBytes").GetInt32();
                    int modulus = fill.GetProperty("modulus").GetInt32();
                    bytes = new byte[entry.GetProperty("bytes").GetInt32()];
                    Assert.Equal(bytes.Length, header.Length + payloadLength);
                    header.CopyTo(bytes, 0);
                    for (int i = 0; i < payloadLength; i++) { bytes[header.Length + i] = (byte)(i % modulus); }
                }
                Assert.Equal(entry.GetProperty("bytes").GetInt32(), bytes.Length);
                Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
                return bytes;
            });
    }
}

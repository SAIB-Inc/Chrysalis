using Chrysalis.Network.Core;

namespace Chrysalis.Network.Tests.Receive;

internal static class ReceiveSchedules
{
    private const ProtocolType Fetch = ProtocolType.BlockFetch;
    private const ProtocolType Chain = ProtocolType.NodeChainSync;
    private const ProtocolType Keep = ProtocolType.KeepAlive;
    private const ProtocolType Local = ProtocolType.ClientChainSync;
    private static readonly ProtocolType[] Protocols = [Fetch, Chain, Keep, Local];

    internal static ReceiveSchedule Random(int seed)
    {
        ScheduleRandom random = new(seed);
        ReceiveSchedule schedule = new("seed-" + seed);
        int protocolCount = 1 + random.Next(4);
        for (int p = 0; p < protocolCount; p++)
        {
            schedule.Consumers.Add(Protocols[p], (ReceiveConsumer)random.Next(5));
        }
        // Interleave independent protocol streams. Each run has one or several messages; the
        // cuts are deliberately unrelated to message boundaries and include empty segments.
        for (int turn = 0; turn < 3 + random.Next(7); turn++)
        {
            ProtocolType protocol = Protocols[random.Next(protocolCount)];
            int[] lengths = [0, 1, 23, 24, 255, 256, 1000, 65500];
            byte[][] messages = [.. Enumerable.Range(0, 1 + random.Next(4)).Select(_ => ReceiveVectors.Body(lengths[random.Next(lengths.Length)]))];
            int length = messages.Sum(message => message.Length);
            List<int> cuts = [];
            if (random.Next(3) == 0) { cuts.Add(0); }
            while (length > 0)
            {
                int size = random.Next(4) == 0 ? 1 : random.Next(1, Math.Min(length, ushort.MaxValue) + 1);
                cuts.Add(size);
                length -= size;
            }
            schedule.Stream(protocol, messages, [.. cuts]);
            if (random.Next(4) == 0) { schedule.Segment((ProtocolType)99, [.. ReceiveVectors.Body()]); }
        }
        // Across the fixed seed set, force the maximum size as well as the random interior.
        if (seed % 16 == 0)
        {
            byte[] message = ReceiveVectors.Body(65500);
            schedule.Stream(Fetch, [message, message], ushort.MaxValue, (2 * message.Length) - ushort.MaxValue);
        }
        if (seed % 13 == 0)
        {
            byte[] spanning = ReceiveVectors.Body(1000);
            schedule.Stream(Fetch, [spanning], [.. Split(spanning.Length, 100)]);
        }
        if (seed % 19 == 1)
        {
            ProtocolType protocol = Protocols[random.Next(protocolCount)];
            schedule.Segment(protocol, ReceiveVectors.Body(23)[..^1]);
            schedule.DeclaredDifference = "EOF inside message (random schedule)";
        }
        schedule.Interleave(random);
        int wireLeft = schedule.Wire.Count;
        if (seed % 17 == 1)
        {
            wireLeft--;
            schedule.DeclaredDifference = "EOF inside segment (random schedule)";
        }
        int steps = 0;
        while (wireLeft > 0)
        {
            if (random.Next(3) != 0 || steps++ > 200)
            {
                int take = Math.Min(wireLeft, random.Next(1, Math.Min(wireLeft, 100000) + 1));
                schedule.Steps.Add(new(ReceiveAction.Deliver, Bytes: take));
                wireLeft -= take;
            }
            ProtocolType protocol = Protocols[random.Next(protocolCount)];
            schedule.Call(protocol);
            if (random.Next(12) == 0) { schedule.Steps.Add(new(ReceiveAction.Cancel, protocol)); }
        }
        // Some consumers intentionally stop early. Others drain enough calls to expose shared
        // items and partial messages. End/Dispose then leave deterministic parked or end states.
        // Declared EOF differences must reach EOF. Notification consumers need two calls per item,
        // and a canceled ReadAll iterator cannot drain; those stop/cancel mixes remain in other seeds.
        if (schedule.DeclaredDifference is not null)
        {
            _ = schedule.Steps.RemoveAll(step => step.Action == ReceiveAction.Cancel && schedule.Consumers[step.Protocol] == ReceiveConsumer.All);
        }
        int calls = seed % 7 == 0 && schedule.DeclaredDifference is null ? 0 : schedule.Segments.Count + schedule.Messages.Count + 2;
        for (int call = 0; call < calls; call++)
        {
            foreach (ProtocolType protocol in schedule.Consumers.Keys)
            {
                schedule.Call(protocol, schedule.DeclaredDifference is not null && schedule.Consumers[protocol] == ReceiveConsumer.Wait ? 2 : 1);
            }
        }
        schedule.Steps.Add(new(seed % 11 == 0 && schedule.DeclaredDifference is null ? ReceiveAction.Dispose : ReceiveAction.End));
        foreach (ProtocolType protocol in schedule.Consumers.Keys) { schedule.Call(protocol, 2); }
        return schedule;
    }

    internal static ReceiveSchedule Named(int number, ReceiveConsumer variant = ReceiveConsumer.Buffer)
    {
        ReceiveSchedule schedule = new($"H{number}-{variant}");
        schedule.Consumers[Fetch] = variant;
        byte[] a = ReceiveVectors.Body();
        byte[] b = ReceiveVectors.Body(23);
        switch (number)
        {
            case 1:
                schedule.Consumers[Keep] = ReceiveConsumer.Read;
                schedule.Stream(Fetch, [a, b, a], a.Length + 2, b.Length - 2 + a.Length);
                AddSingleSegments(schedule, Fetch, 63);
                schedule.Stream(Keep, [a], a.Length);
                schedule.Call(Keep);
                schedule.DeliverAll();
                schedule.Call(Fetch); // A; start of B remains, and the freed slot admits segment 64.
                schedule.Call(Fetch, 2);
                break;
            case 2:
                schedule.Consumers[Fetch] = ReceiveConsumer.Poll;
                byte[] longMessage = ReceiveVectors.Body(256);
                schedule.Stream(Fetch, [longMessage], [.. Split(longMessage.Length, 100)]);
                schedule.DeliverAll();
                schedule.Call(Fetch, 102);
                break;
            case 3:
                schedule.Consumers[Fetch] = variant;
                AddSingleSegments(schedule, Fetch, 65);
                schedule.Call(Fetch); // WaitToReadAsync must not remove the item it announces.
                schedule.DeliverAll();
                schedule.Call(Fetch, 132);
                break;
            case 4:
                schedule.Consumers[Fetch] = ReceiveConsumer.Read;
                AddSingleSegments(schedule, Fetch, 65);
                for (int i = 0; i < 7; i++) { schedule.Segment((ProtocolType)99, new byte[ushort.MaxValue]); }
                schedule.DeliverAll();
                schedule.Call(Fetch); // A separate return checkpoint is recorded by the runner.
                schedule.Call(Fetch, 65);
                break;
            case 5:
                schedule.Consumers[Fetch] = ReceiveConsumer.Read;
                schedule.Consumers[Chain] = ReceiveConsumer.Buffer;
                AddSingleSegments(schedule, Fetch, 65);
                schedule.Stream(Chain, [a, b, a], (2 * a.Length) + b.Length);
                schedule.Call(Chain);
                schedule.DeliverAll();
                schedule.Call(Fetch);
                schedule.Call(Chain, 3);
                schedule.Call(Fetch, 65);
                break;
            case 6:
                schedule.Consumers[Keep] = ReceiveConsumer.Read;
                for (int i = 0; i < 65; i++) { schedule.Stream(Fetch, [a, b, a], (2 * a.Length) + b.Length); }
                schedule.Stream(Keep, [a], a.Length);
                schedule.Call(Keep);
                schedule.DeliverAll();
                schedule.Call(Fetch, 198);
                break;
            case 7:
                schedule.Consumers.Clear();
                schedule.Consumers[Chain] = ReceiveConsumer.Buffer;
                schedule.Consumers[Fetch] = ReceiveConsumer.Read;
                foreach (int count in new[] { 70, 10, 1 })
                {
                    schedule.Stream(Chain, [.. Enumerable.Repeat(a, count)], a.Length * count);
                }
                schedule.Stream(Fetch, [b], b.Length);
                schedule.Call(Fetch);
                schedule.DeliverAll();
                schedule.Call(Chain, 16);
                schedule.Call(Chain, 66);
                break;
            case 8:
                schedule.Consumers.Clear();
                schedule.Consumers[Local] = variant;
                AddSingleSegments(schedule, Local, 4);
                byte[] localMessage = ReceiveVectors.Body(65500);
                schedule.Stream(Local, [localMessage], [.. Split(localMessage.Length, 100)]);
                schedule.DeliverAll();
                schedule.Call(Local, 110);
                break;
            case 9:
                for (int i = 0; i < 70; i++) { schedule.Stream(Fetch, [a], 0, a.Length, 0); }
                schedule.DeliverAll();
                schedule.Call(Fetch, 220);
                break;
            case 10:
                schedule.Consumers[Fetch] = ReceiveConsumer.Read;
                schedule.Consumers[Keep] = ReceiveConsumer.Read;
                AddSingleSegments(schedule, Fetch, 65);
                schedule.Stream(Keep, [a], a.Length);
                schedule.Call(Keep);
                schedule.DeliverAll(); // No fetch calls: both systems must remain stopped here.
                schedule.Steps.Add(new(ReceiveAction.End));
                return schedule;
            case 11:
                schedule.Consumers[Fetch] = ReceiveConsumer.Read;
                schedule.Stream(Fetch, [a], a.Length);
                schedule.Call(Fetch);
                schedule.Steps.Add(new(ReceiveAction.Cancel, Fetch));
                schedule.DeliverAll();
                schedule.Call(Fetch);
                break;
            case 12:
                schedule.Consumers[Fetch] = ReceiveConsumer.Read;
                schedule.Consumers[Keep] = ReceiveConsumer.Read;
                AddSingleSegments(schedule, Fetch, 4);
                schedule.DeliverAll();
                schedule.Call(Keep);
                schedule.Steps.Add(new(ReceiveAction.Dispose));
                schedule.Call(Fetch, 6);
                schedule.Call(Keep);
                return schedule;
            case 13:
                schedule.DeclaredDifference = "EOF inside segment: legacy spins; shared reader faults EndOfStreamException";
                schedule.Stream(Fetch, [a], a.Length);
                schedule.Steps.Add(new(ReceiveAction.Deliver, Bytes: schedule.Wire.Count - 1));
                schedule.Call(Fetch);
                schedule.Steps.Add(new(ReceiveAction.End));
                return schedule;
            case 14:
                schedule.DeclaredDifference = "EOF inside message: legacy consumer ChannelClosedException; shared-reader connection EndOfStreamException";
                schedule.Segment(Fetch, b[..^1]);
                schedule.Call(Fetch);
                schedule.DeliverAll();
                schedule.Steps.Add(new(ReceiveAction.End));
                return schedule;
            case 15:
                schedule.DeclaredDifference = variant == ReceiveConsumer.Buffer
                    ? "Invalid CBOR: legacy consumer CborException; shared-reader connection CborException"
                    : "Over N2N cap: legacy accepts; shared-reader connection InvalidDataException";
                schedule.Consumers[Fetch] = ReceiveConsumer.Buffer;
                if (variant == ReceiveConsumer.Buffer) { schedule.Segment(Fetch, [0xff]); }
                else
                {
                    byte[] overCap = ReceiveVectors.Body(2500000);
                    schedule.Stream(Fetch, [overCap], [.. Split(overCap.Length, 40)]);
                }
                schedule.Call(Fetch);
                schedule.DeliverAll();
                schedule.Steps.Add(new(ReceiveAction.End));
                return schedule;
            default: throw new ArgumentOutOfRangeException(nameof(number));
        }
        schedule.Steps.Add(new(ReceiveAction.End));
        foreach (ProtocolType protocol in schedule.Consumers.Keys) { schedule.Call(protocol, 2); }
        return schedule;
    }

    private static void AddSingleSegments(ReceiveSchedule schedule, ProtocolType protocol, int count)
    {
        byte[] message = ReceiveVectors.Body();
        for (int i = 0; i < count; i++) { schedule.Stream(protocol, [message], message.Length); }
    }

    private static IEnumerable<int> Split(int bytes, int pieces)
    {
        for (int i = 0; i < pieces; i++) { yield return (bytes / pieces) + (i < bytes % pieces ? 1 : 0); }
    }
}

// Deterministic schedule choices, not security randomness. The seed is the reproduction key.
internal sealed class ScheduleRandom(int seed)
{
    private uint _state = (uint)seed + 1;
    internal int Next(int maximum) => Next(0, maximum);
    internal int Next(int minimum, int maximum)
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return minimum + (int)(_state % (uint)(maximum - minimum));
    }
}

# Received messages and connection ends

## Keep received bytes

`ChannelBuffer.ReceiveFullMessageAsync<T>` keeps its signature. Returned records
now retain their bytes after later receives, including messages split across mux
segments. A defensive copy used only to survive the next receive is unnecessary.

Before:

```csharp
var message = await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(token);
ReadOnlyMemory<byte> saved = message.Raw.ToArray();
```

After:

```csharp
var message = await buffer.ReceiveFullMessageAsync<BlockFetchMessage>(token);
ReadOnlyMemory<byte> saved = message.Raw;
```

For an `AgentChannel` constructed with a caller-made channel, never modify or
reuse an item after queuing it. A returned message may retain a slice of that
item. A message spanning items gets its own array; this adds one owned-message
allocation compared with the old reused buffer. The staging buffer remains
allocated at its largest capacity for the connection's lifetime.

## Read raw queue items

Search for `Demuxer.Subscribe` and `AgentChannel.ReadSegmentAsync`. On Demuxer/Plexer-backed channels, each call
still consumes one item per mux segment, but an item now holds all whole CBOR
messages that segment completed, back to back. A segment completing no message
produces an empty item; the next completing segment's item includes the entire
spanning message. Do not interpret an item as the original segment's bytes.

Before:

```csharp
AppendSegment(await channel.ReadSegmentAsync(token));
```

After:

```csharp
ReadOnlyMemory<byte> completed = await channel.ReadSegmentAsync(token);
if (!completed.IsEmpty) { ProcessCompleteMessages(completed); }
```

Caller-provided readers are forwarded unchanged by `ReadSegmentAsync`: their
items need not contain whole messages, and their own completion exceptions remain.

The functions in these raw-item examples stand for the consumer's own framing
and processing. Prefer `ChannelBuffer` when receiving typed protocol messages.
One protocol permits only one outstanding `ReadAsync` or `WaitToReadAsync`
across both members; a second now throws `InvalidOperationException`.
A full 64-item protocol queue still stalls all protocols until it is consumed.

## Handle connection ends

`ConnectionClosedException`, in `Chrysalis.Network.Multiplexer`, derives from
`ChannelClosedException`. Existing catches of `ChannelClosedException` still
match. On Demuxer/Plexer-backed channels, pending and later receive calls drain queued messages first, then throw
`ConnectionClosedException`; `InnerException` is the first end's cause, or null
for a clean peer close.

Before:

```csharp
catch (ChannelClosedException) { HandleConnectionEnd(null); }
```

After, for a Demuxer/Plexer-backed channel when the cause matters:

```csharp
catch (ConnectionClosedException error) { HandleConnectionEnd(error.InnerException); }
```

Caller-made channels retain their reader's own errors, including
`ChannelClosedException`, and report malformed CBOR directly as `CborException`;
do not replace their catches with `ConnectionClosedException`. On caller-made
channels the N2N cap throws `InvalidDataException` directly, with no connection
to end. A CBOR or cap failure stays failed on later receives.

On Demuxer/Plexer-backed channels, invalid CBOR now ends the entire connection with a `CborException` cause, rather
than failing only that protocol's consumer. N2N messages on protocol IDs 0, 2,
3, 4, 8 and 10 have a 2,500,000-byte cap; exceeding it ends the connection with
an `InvalidDataException` cause. N2C IDs 5, 6, 7 and 9 remain uncapped. A truncated
segment or message ends with an `EndOfStreamException` cause instead of leaving
the reader spinning or silently losing partial input.

Raw and batch sends after an end throw `ConnectionClosedException`, including
after local disposal, instead of accessing a disposed bearer lock. `Dispose`
returns without blocking and may close the socket one thread-pool hop later,
after active readers and writers leave. The muxer and the public `AgentChannel`
constructor remain in this MR; their send-path replacement belongs to MR3.

## Standalone demuxers and protocol ids

`Demuxer.Subscribe`, `Plexer.SubscribeClient` and `SubscribeServer` now reject
protocol ids not listed in `ProtocolType` with `ArgumentOutOfRangeException`.
Constructing a `ChannelBuffer` for an unknown caller-provided protocol id also
throws that exception; the old receive path accepted arbitrary ids.

A standalone `Demuxer(IBearer)` still does not own or dispose its bearer. However,
every connection end, including `Dispose`, EOF and a read fault, cancels the
bearer writer's pending or next flush via `CancelPendingFlush`; a caller sharing
that writer must handle the canceled flush. `Demuxer.Dispose` now stops and
completes `RunAsync` instead of leaving its read loop running. Calling `RunAsync`
a second time throws `InvalidOperationException`.

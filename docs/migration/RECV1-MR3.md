# Network: write directly to one bearer

`Muxer` and its input pipe are gone. Prefer `Plexer.SubscribeClient(protocol)` or
`Plexer.SubscribeServer(protocol)` over constructing a channel. `Plexer.RunAsync`
still starts the reader. Sends no longer need a background muxer to be running;
await the send itself to observe bearer backpressure or a transport fault.

Before:

```csharp
using var muxer = new Muxer(bearer.Writer, ProtocolMode.Initiator, writeLock);
var channel = new AgentChannel(protocol, muxer.Writer, bearer.Writer, incoming, writeLock);
Task muxLoop = muxer.RunAsync(connectionToken);
await channel.EnqueueChunkAsync(payload, sendToken);
```

After, for a caller-managed reader and writer:

```csharp
var channel = new AgentChannel(protocol, bearer.Writer, incoming, writeLock);
await channel.EnqueueChunkAsync(payload, sendToken);
```

Use the same `SemaphoreSlim(1, 1)` for all channels on one bearer, and a separate
lock for every independent bearer. The caller still owns the writer, reader and
lock when constructing channels this way. A send fault is permanent for the lock;
use a new lock for a new bearer. For responder frames, pass
`mode: ProtocolMode.Responder`; `Plexer.SubscribeServer` does this automatically.
Raw sends keep all bytes supplied by the caller, including the direction bit.

Muxed sends after the connection ends now throw `ConnectionClosedException`
instead of `InvalidOperationException`. Change filters for the old stop message:

```csharp
// Before
catch (InvalidOperationException ex) when (ex.Message == "The muxer has stopped accepting protocol messages.")
{
    OnDisconnect(ex.InnerException);
}

// After
catch (ConnectionClosedException ex)
{
    OnDisconnect(ex.InnerException);
}
```

`ConnectionClosedException` derives from `ChannelClosedException`.
`InnerException` is the connection's first fault, or `null` for a clean peer
close. A failed send ends a `Plexer` connection and also notifies its receivers.
A muxed payload larger than 65,535 bytes fails before writing with
`ArgumentOutOfRangeException`. `WriteBatchRawSegmentsAsync` rejects a batch over
65,543 bytes including headers with `ArgumentOutOfRangeException` before writing.
`ChainSync.SendNextRequestBatchAsync` therefore rejects depths above 6,554 with
`ArgumentOutOfRangeException` before writing any requests.
Cancellation before commit writes nothing; after commit the caller's token does
not interrupt the bearer flush (or the rest of a raw batch). Connection end can
stop a batch between copies. A flush failure does not prove that the peer did not
receive bytes.

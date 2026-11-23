# Connection Completion, one fault contract, and `ValueTask` receives

## `Completion` on `Plexer`, `PeerClient`, and `NodeClient`

`Plexer`, `PeerClient`, and `NodeClient` now expose `Task Completion`. It stays
pending while the connection is open and settles once when the connection ends:

- Local `Dispose` / `DisposeAsync` (including after a failed `StartAsync` that
  saw no prior connection fault) and `Plexer.RunAsync` token cancellation:
  `Completion` runs to completion (`TaskStatus.RanToCompletion`).
- Clean peer close at a boundary: `Completion` faults with
  `ConnectionClosedException` (`InnerException` is `null`).
- Any fault (truncated segment or message, read or send `IOException`, invalid
  CBOR, N2N size cap, CBOR decode error, `ReplyTimeoutException`, mid-message
  cancelled send, or keep-alive protocol error): `Completion` faults with
  `ConnectionClosedException` whose `InnerException` is that first cause.

The first end wins: a later `Dispose` or a later keep-alive error never replaces
the recorded cause.

## `Plexer.RunAsync`, health checks, and exception getters

- `Plexer.RunAsync` now returns normally after `Dispose`, throws
  `OperationCanceledException` when its own token cancels, and throws
  `ConnectionClosedException` on a clean peer close or any fault. Previously it
  threw `InvalidOperationException("Demuxer completed unexpectedly without error")`
  after `Dispose` and clean peer close, and raw causes on faults.
- `GetPlexerException()` returns the first cause exception on a fault, the
  `ConnectionClosedException` itself on a clean peer close, and `null` while
  running or after a local end.
- `PeerClient.IsKeepAliveHealthy()` now turns `false` as soon as `Completion`
  settles, even if the keep-alive loop is idle waiting for its next tick (the
  idle loop exits quietly at its next tick or on disposal).
- `PeerClient.GetKeepAliveException()` now returns the `ReplyTimeoutException`
  directly when a keep-alive reply times out (previously it returned the
  wrapping `ConnectionClosedException`), matching `GetPlexerException()`.
- A keep-alive protocol error (such as a cookie mismatch) now ends the entire
  connection with that error instead of faulting only the keep-alive loop. An
  external connection close stops the keep-alive loop quietly
  (`GetKeepAliveException()` returns `null`).

## `StartAsync` after `Dispose`

Calling `StartAsync` on a disposed `NodeClient` or `PeerClient` now throws
`ObjectDisposedException` on both clients (previously `NodeClient` threw
`ConnectionClosedException` and a second call on either client threw
`InvalidOperationException`). Pending and later mini-protocol calls continue to
throw `ConnectionClosedException` with `ObjectDisposedException` inside.

## Decode errors in `ChannelBuffer.ReceiveFullMessageAsync`

When CBOR deserialization throws on a received frame, `ChannelBuffer` now clears
its receive cursor instead of leaving the undecodable bytes for the next call.
On `Plexer`/`Demuxer`-backed channels it also ends the connection with that
decode error (matching `ouroboros-network`, which tears down the connection on
any mini-protocol decode failure), and later calls throw
`ConnectionClosedException`. On caller-made channels the decode error remains
sticky for later receives.

In the `LocalStateQuery` fresh-state helpers (`GetTipAsync`, `GetCurrentEraAsync`,
`GetSystemStartAsync`, `GetEraHistoryAsync`, `GetCurrentProtocolParamsAsync`,
`GetUtxosByAddressAsync`, and `GetUtxosByTxInAsync`), when a query fails, the
original exception (such as `CborDecodeException`) reaches the caller: a
`ConnectionClosedException` or `OperationCanceledException` from the cleanup
release no longer replaces it (and after a cancelled unwind `IsAcquired` stays
`true` so the next call releases first). When a query succeeds, a cleanup
release failure still propagates.

## `ChannelBuffer.ReceiveFullMessageAsync<T>` returns `ValueTask<T>`

`ChannelBuffer.ReceiveFullMessageAsync<T>` now returns `ValueTask<T>` backed by
a reusable per-buffer waiter so waiting receives allocate nothing on the hot
path. Callers that `await` it compile unchanged; rebuild. Callers that store or
pass a `Task<T>` add `.AsTask()`. Only one receive may be outstanding per
`ChannelBuffer` at a time: await each returned `ValueTask<T>` at most once and
before starting the next receive, or the overlapping call faults with
`InvalidOperationException` without disturbing the active receive.

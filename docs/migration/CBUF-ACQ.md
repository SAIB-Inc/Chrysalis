# Cancelled sends and failed acquire state

## `ChannelBuffer.SendFullMessageAsync` throws on cancellation

`ChannelBuffer.SendFullMessageAsync` now throws `OperationCanceledException` when
`cancellationToken` is cancelled before sending or between segments of a
multi-segment message. Previously it broke out of its send loop and returned
normally without sending the message (or after sending only earlier segments).

- Send-only calls (`ChannelBuffer.SendFullMessageAsync`,
  `LocalStateQuery.ReleaseAsync`, `KeepAliveClient.SendKeepAliveRequestAsync` and
  `SendDoneAsync`, `KeepAliveServer.SendKeepAliveResponseAsync`,
  `ChainSync.DoneAsync`, and `BlockFetch.RequestRangeAsync` and `DoneAsync`) now
  throw `OperationCanceledException` rather than completing silently, and protocol
  state fields are not advanced when the send did not complete. In particular,
  `LocalStateQuery.ReleaseAsync` leaves `IsAcquired` true if cancelled before
  sending `MsgRelease`.
- Send-then-receive calls (`LocalStateQuery.QueryAsync` and `AcquireAsync`,
  `LocalTxMonitor` queries and `AcquireAsync`, `LocalTxSubmit.SubmitTxAsync`,
  `ChainSync.FindIntersectionAsync`, and `Handshake.SendAsync`) already threw
  `OperationCanceledException` on the receive step; they now throw before sending
  (and before advancing protocol state).
- The `LocalStateQuery` helpers (`GetTipAsync` and the other extension methods
  through `ExecuteWithFreshStateAsync`) may now surface
  `OperationCanceledException` from their `finally` release step when the token is
  cancelled.
- After an `OperationCanceledException` from a send larger than one mux segment
  (65,535 payload bytes), earlier segments may already be on the wire: discard the
  connection rather than reusing it.

## `LocalStateQuery.AcquireAsync` clears `IsAcquired` on failure

When `LocalStateQuery.AcquireAsync` is called while `IsAcquired` is already true
and the node answers `MsgFailure` (or an unexpected reply), `IsAcquired` is now
cleared to `false` before `InvalidOperationException` is thrown, matching the
Ouroboros LocalStateQuery transition back to `StIdle`. Against a real node, the
acquired-then-`MsgFailure` path is reachable only once re-acquire (`MsgReAcquire`)
is sent (N7). If the send itself is cancelled before completing, `IsAcquired` is
left unchanged.

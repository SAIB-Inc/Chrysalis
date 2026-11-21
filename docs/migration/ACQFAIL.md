# Local state query acquire failure reason

## Wire format of `FailureReason` leaf records

The node sends `MsgFailure` as `[2, 0]` (`AcquireFailurePointTooOld`) or `[2, 1]`
(`AcquireFailurePointNotOnChain`), with a bare unsigned integer reason rather than
a one-element list. `AcquireFailurePointTooOld` and `AcquireFailurePointNotOnChain`,
in `Chrysalis.Network.Cbor.LocalStateQuery.Messages`, now encode and decode as bare
integers (`0` and `1`) instead of `[0]` and `[1]`.

This is a breaking wire-format change for callers that serialised or deserialised
the leaf records directly: the old `[0]` and `[1]` list forms are now rejected with
`CborDecodeException`, and an out-of-range or mismatched `Idx` value is rejected
with `CborValidationException` on both read and write. The public record types and
signatures (`Failure`, `FailureReason`, `AcquireFailurePointTooOld`,
`AcquireFailurePointNotOnChain`) are unchanged.

## `LocalStateQuery.AcquireAsync` failure

When the node rejects an acquire request with `MsgFailure`,
`LocalStateQuery.AcquireAsync` now throws the documented `InvalidOperationException`
(`Failed to acquire state: AcquireFailurePointTooOld.` or
`Failed to acquire state: AcquireFailurePointNotOnChain.`), leaves `IsAcquired`
false, and consumes the reply so the handler's receive buffer stays in step for
subsequent messages. Before this fix, `AcquireAsync` threw `CborDecodeException`
and left the undecoded reply in the buffer. Code that caught `CborDecodeException`
around `AcquireAsync` for acquire rejections catches `InvalidOperationException`
instead.

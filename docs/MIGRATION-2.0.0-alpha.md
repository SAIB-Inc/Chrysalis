# Migration to 2.0.0-alpha

This is an unpublished candidate. Public changes are made for protocol correctness, not tidiness. Source and binary compatibility breaks require rebuilding callers. The capability claims and provisional N2C default are recorded in `MANIFEST.md`.

## Five approved node-to-client type-change groups

The first four groups are approved by D5 and belong to B2–B4; listing them here does not claim those repairs have landed in this B1 change.

1. **LocalTxMonitor.ReleaseAsync**: the repaired API returns `Task`, not a task containing a protocol reply: Release has no server response. Await completion of the send, then reacquire if needed (B4).
2. **Mempool measures**: the measures element type changes from 32-bit to 64-bit to represent actual node capacities without overflow. Update collection element types and avoid narrowing conversions (B4).
3. **Result.QueryResult**: the result becomes an inline raw CBOR value instead of the incorrect outer representation. Decode the query-specific ledger payload from the inline value (B2).
4. **RejectTx.RejectReason**: the reason becomes inline raw CBOR instead of the incorrect byte-string representation. Decode the structured node rejection without assuming that the node supplied a CBOR byte string (B3).
5. **HandshakeDecodeError.ErrorData and Refused.ErrorData**: both members change from `ReadOnlyMemory<byte>` to **`string`** (D26, B1). The node sends CBOR text, so read `ErrorData` directly; explicitly UTF-8 encode only when bytes are required by a caller. The member names are retained. There is no `ErrorText` accessor, converter bridge or generator change. A fresh record writes CBOR text, and CBOR byte-string/null payloads are rejected. This fifth group has its own described commit boundary because retaining the byte declaration could never decode a node's actual text and would require unnecessary generator support.

The two text members form the single fifth type-change group and a single D26 boundary. The other boundaries above match the individually described public deltas; implementation/test/provenance and documentation changes do not authorize unrelated API changes.

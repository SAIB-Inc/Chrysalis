# Unknown era tags

`BlockWithEra` and `N2NBlockHeader` now reject unmapped wire tags with `UnknownEraException : FormatException` before reporting a decoded envelope. Code that relied on lazy acceptance, a union fallback, message text, or an `InvalidOperationException` catch must change.

## Before

```csharp
BlockWithEra envelope = CborSerializer.Deserialize<BlockWithEra>(bytes);
IBlock block = envelope.Block;
```

Reading the envelope previously did not establish that its era was supported. An unmapped tag could reach the ordinary union fallback when the body was accessed.

## After

```csharp
try
{
    BlockWithEra envelope = CborSerializer.Deserialize<BlockWithEra>(bytes);
    IBlock block = envelope.Block;
}
catch (UnknownEraException error)
{
    UnknownEra unprocessed = error.Value;
    ReadOnlyMemory<byte> originalEnvelope = unprocessed.Raw;
}
```

The same catch applies to `CborSerializer.Deserialize<N2NBlockHeader>(headerBytes)`. Inspect `WireTag`, `Envelope`, `Raw`, and `Point` on the error. `Raw` is an owned copy and remains available after the input buffer is reused. An unsupported item is never represented as a decoded block or header.

For an explicitly unsupported item, receive unprocessed bytes directly:

```csharp
UnknownEra block = BlockWithEra.ReadUnknownEra(bytes);
UnknownEra header = N2NBlockHeader.ReadUnknownEra(headerBytes);
```

These raw-only readers reject supported tags; when an input might be supported, use the typed reader and catch the error instead. Both definite and indefinite unknown envelopes must contain exactly a tag and a body. Malformed envelopes are rejected rather than retained as incomplete unknown values. The retained bytes cover one complete envelope, not any following input.

`WireTag` is the original unsigned wire number, not the `Era` enum. Block and N2N-header numberings differ: Conway is block tag **7** and header tag **6**. Do not convert an unknown tag by adding or subtracting an era offset.

No point is inferred from an unknown layout. `Point` is null unless the caller supplies known context. Pass `(slot, hash)` to `BlockWithEra.Read(bytes, (slot, hash))` or `N2NBlockHeader.Read(headerBytes, (slot, hash))` to attach it to an error. The raw-only readers accept the same optional context. Built-in network operations do not attach that point or expose a raw receive option in this change. Raw mode is available only for bytes the caller already holds (including bytes retained in an UnknownEraException). Network-side point attachment and raw delivery are separate N-proto work. The supplied point hash remains the caller's memory; only Raw is copied.

## Additional validation changes

- Definite and indefinite era envelopes with a member count other than two now throw `FormatException` from `Read`. The generic serializer may wrap that exception in its existing `InvalidOperationException`. Validated fixed-size lists, including Byron main blocks, now reject an incorrect definite length or an extra/missing indefinite member too.
- A Byron body with an invalid main-block body proof now throws when `.Block` materializes the `ByronMainBlock`; it is not accepted as a main block. The check reads the proof's CBOR shape without materializing the header.
- `BlockWithEra.Create` and `N2NBlockHeader.Create` with an unmapped tag now throw `UnknownEraException`, because creation uses the same validated read path.
- `ByronMainBlockValidator` changed from a public class to a public readonly record struct. This is a binary-breaking change; recompile consumers.
- Standard `UnknownEraException` constructors without wire data leave `Value` at its default; that default is not an observed wire tag or point. Errors thrown while decoding retain their complete wire value.

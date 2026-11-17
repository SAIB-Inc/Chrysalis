# Hot paths

Area-to-benchmark coverage is defined once in `gate/gated.json`; the gate prints it.
For alpha 1 every area is touched. D51 requires both sync-path and other-hot-path
Fly timings; PC time is never gating. UTxO RPC has D20's manual first measurement.

Counts below are for **7c45d47**, under `src/Chrysalis.Network/`.
An async method has the `async` modifier and executes on the per-message path;
Task/ValueTask-returning wrappers without it do not count. An async iterator's
body executes per yielded message; a range setup call is not charged per body.
A hand-off means a continuation queued to the thread pool, never an inline one.
Counts are source-path counts, not claims about an observed scheduling trace.

## Network receive

| Path | Async methods | Loop resumes | Hand-offs |
|---|---:|---|---|
| N2N header, one request in flight | 3 | Demuxer per bearer read, plus full-channel waits | w_h + d_h |
| N2N body, direct receive iterator | 2 | Same | w_b + d_b + q_b |
| N2N body through FetchRangeAsync | 3 | Same | Same |
| N2C RollForward, one segment | 3 | Same | w_c + d_c |

Header/N2C receive: `MiniProtocols/ChainSync.cs:119,129,182` and
`Multiplexer/ChannelBuffer.cs:68,83,119`. These are NextRequestAsync,
ReceiveNextResponseAsync and ReceiveFullMessageAsync. The request's send is
outside the bearer-read-to-caller receive count. A queued reply can complete
without a channel wake; “2” is conditional on one waiting bearer read (R1) and
one parked channel reader (R2), not an unconditional per-header count.

Body: `MiniProtocols/BlockFetch.cs:72,101` or `:126,156` receives each body through
ReceiveFullMessageAsync. `FetchRangeAsync` (`:184,189`) adds an outer async iterator,
so the gate's WholePath caller (`Chrysalis.Benchmarks/WholePath.cs:149`) has three,
not two. `FetchSingleAsync` (`BlockFetch.cs:202,207`) similarly adds one wrapper;
RequestRangeAsync (`:53`) is once per range, not once per body in a multi-block range.

Non-async wrapper, not counted: `AgentChannel.ReadSegmentAsync` (`:91`).
Demuxer.RunAsync (`:47,56`) is a connection loop, not a per-message async method.
R1 is a waiting bearer read; R2 is a delivery to a parked channel reader
(`Demuxer.cs:88`); R3 is a demuxer write waiting for space in its 64-segment channel
(`:31-36,88`). For each segment, R2 and R3 are alternatives. Thus body hand-offs
are w + d + q, not just w + d when the queue can saturate. Each count may be zero;
multiple messages may share one read, and one message may span multiple reads.
Plexer.RunAsync (`:53,57-66`) is lifecycle-only, not resumed once per bearer read.

## Network send

| Path | Async methods | Loop resumes | Hand-offs |
|---|---:|---|---|
| One single-segment muxed send | 4 | Muxer per pipe read | S2, plus conditional S1 |

The four are FindIntersectionAsync (`ChainSync.cs:96,105`, send portion only),
SendFullMessageAsync (`ChannelBuffer.cs:46,61`), EnqueueChunkAsync
(`AgentChannel.cs:29,43`) and WriteSegmentAsync (`Muxer.cs:41,102`). Larger messages
invoke the last two per chunk, not once per message. Muxer.RunAsync (`:72,80`) is
a connection loop. S2 is a parked pipe reader being scheduled by the flush;
sends can share it or find buffered input. S1 is only a contended bearer lock or
asynchronous socket write (`Muxer.cs:45,50,53`; raw sends `AgentChannel.cs:54,57-58`).
NextRequest's pre-encoded raw send bypasses the muxer (`ChainSync.cs:127`,
`ChannelBuffer.cs:35`, `AgentChannel.cs:52`), so it has no S2.

## CBOR decode
Generated readers use SAIB.Cbor. `FieldReadBench.ReadEveryField` already supplies
the every-field direct row, with typed visitors primed outside measured work.
## CBOR encode and re-serialisation
Writers and preserved-raw serialization use the gated direct rows.
## Hashing
Blake256.
## Signing and addresses
Sign, Verify and address encode/decode.
## Transaction building and fees
TxBench.
## Plutus
VmBench and CorpusVmBench.
## UTxO RPC
D20: attach the first measurement for alpha 1; no invented baseline.

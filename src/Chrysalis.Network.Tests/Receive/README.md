# RECV1 differential harness

The suite compares the frozen fc21a49 receive path with the current shared reader.
The historical phase-1 harness compared fc21a49 with itself before the reader existed.
`FixedSeedsMatchFrozenFc21a49` runs seeds 0–1999 by default. `RECV1_SEEDS`
selects the larger prototype run. RECV1_SEED repeats one seed that many times
for race reproduction; without it the fixed sequence remains 0–N−1. Failures print
the seed, repetition and complete reproduction manifest. `RECV1_MANIFEST_DIR`, when supplied by a local evidence run, records
one JSON manifest per schedule. Neither variable changes library behavior.

## Inputs and the reference

`Fixtures/Receive/generate.py` creates canonical BlockFetch messages with the
independent Python cbor2 implementation. The recorded manifest contains the
implementation version and each vector's SHA-256; tests verify the hashes.
Large vectors are stored only as cbor2-produced header bytes, the fill rule, full
length and SHA-256. Tests rebuild them and verify that hash before use; no Chrysalis
encoder constructs their headers. Python is not needed to run the tests.
Mux headers are schedule framing, not
asserted golden output from Chrysalis's encoder.

`Frozen/` preserves the release routing loop, receive buffer, scanner and header
decoder. `SOURCE.json` identifies the source commit and original hashes. Only
namespaces/imports are adapted; the header decoder is extracted without its
unused encoder. The oracle must not follow the MR1 scanner or Q7 changes.

The frozen adapter forwards around fc21a49's actual bounded channel. The current
adapter observes the actual ProtocolQueue. Neither simulates admission or replaces
routing policy. Frozen source and SOURCE.json are embedded and hash-checked by
FrozenOracleMatchesThePinnedFc21a49Adaptations; the oracle stays fixed.

## The two comparisons

After every schedule action, comparison 1 checks admitted segment indices,
dropped indices, source frontier, actual slot occupancy, FIFO dequeues, every
logical call's result/pending/error state, WaitToReadAsync's boolean, and the
connection end/cause. Admission completeness is independently checked against
the manifest, not only against the other system.

Comparison 2 checks bytes against the manifest. Item expectations are derived
from the generated cuts, never from a CBOR scanner or MessageAssembler:

| Consumer | Reference | Shared reader |
|---|---|---|
| ChannelBuffer | Snapshot message.Raw at return | Same message bytes, call by call |
| ReadAsync | Manifest segment payload | Messages whose last byte was in that segment |
| TryRead | Same, including empty items | Same completion projection, including empty items |
| WaitToReadAsync then TryRead | Notification does not dequeue | Same indices and notification result |
| ReadAllAsync | One MoveNext per consuming call | Same indices and normalized message stream |

Raw outputs are normalized at manifest message boundaries, ignoring only an
unfinished trailing message on the reference side. Whole-message items must
match exactly, including their total length. A projection test independently checks the intentional payload difference.

## Quiet and ends

The scripted PipeReader returns one delivered boundary at a time. It can expose
a boundary synchronously, complete a parked read through the pool, or invoke a callback
synchronously during registration to force that race. It retains
unconsumed bytes and honors consumed/examined positions. Consumers have at most
one outstanding call per protocol.

A quiet system has every call completed or actually parked, and either a pending
bearer read, a genuinely pending write to a 64-item queue, or a completed run.
A completed channel wait whose continuation has not run is not a parked call.
Likewise a writer's observation window around an already-completed write is not
a full-queue stop. No elapsed sleep is used to decide progress.

For truncated mux EOF, 32 zero-progress completed reads establish the release's
known spin; the driver records that state, cancels its run and disposes its
bearer. H13–H15 and random truncated endings assert their declared connection fault types.
Declared EOF schedules drain and end, rather than substituting a local Dispose or an early full-queue stop; those schedules remain in other seeds.
The driver owns the bearer when it closes, matching today's Plexer ownership.

H4 records the bytes advanced on the dequeuing caller's own thread at return,
so a later pool requeue cannot inflate that checkpoint. The shared-reader budget is asserted at that checkpoint. H16 blocks an actual handler synchronously while checking
both other protocols, with the next read ready first, completing synchronously
during registration, and delivered only after it is pending. A precompleted read
alone does not exercise the callback-registration race.

The delayed-writer regression holds the 65th writer's completion behind a gate,
then drains all 65 admissions and parks a newer ReadAsync or WaitToReadAsync.
Releasing only the older writer must leave that newer wait parked. Notifications
are observed at the actual channel event, never in that delayed acknowledgement.

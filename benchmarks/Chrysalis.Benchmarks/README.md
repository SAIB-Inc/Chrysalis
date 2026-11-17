# Shared Chrysalis performance harness

This is one BenchmarkDotNet project, not two independently maintained benchmarks.
`../run-benchmarks.sh` stages these same C# files twice under `benchmarks/out/`,
selects SDK 10.0.301/net10.0 for the immutable 7c45d47 worktree and SDK
11.0.100/net11.0 for the candidate, and references each tree's actual source
projects and central package versions. Each stage has its own repository-local
`global.json`; no SDK selector is written outside the repository. Source trees
are not modified by staging. Build outputs and the NuGet cache stay under work/.

All builds and executable runs enter `heavy`. The load timestamps are captured
*after* heavy grants a slot, not before its queue. Runs retain command lines,
start/end UTC and 1-minute loads, exit codes, logs, SDK/runtime versions, CPU,
GC configuration, source commits/dirty flags and a hash of the shared harness. The Python runner uses
BSD `sysctl` for CPU/load data on macOS and `/proc` on Linux; C# has no platform-specific counters.
The JSON result and readable `summary.md` distinguish thread-local allocation
checks from BenchmarkDotNet's all-thread MemoryDiagnoser. The summarizer computes
Student-t 95% intervals from BDN's overhead-adjusted result iteration means; it
does not mislabel BDN's default displayed 99.9% interval as 95%.

## Running

From the repository root:

```
benchmarks/run-benchmarks.sh --mode smoke --skip-network
benchmarks/run-benchmarks.sh --mode full --n2n-container
benchmarks/run-benchmarks.sh --mode smoke --measure direct
benchmarks/run-benchmarks.sh --mode full --filter '*BlockBench*' --measure micro
benchmarks/run-benchmarks.sh --mode smoke --measure network --n2n-container
```

Use `--baseline`, `--candidate`, `--output` and `--filter` to select paths/cases.
`--output` must stay inside this repository so SDK selectors cannot escape it;
copy the retained result directory elsewhere for evidence after the run. Both
sides always build; stale-binary reuse is intentionally not a measurement option.
`--pairs N` alternates AB, BA process ordering. Defaults are one microbenchmark
process pair and five whole-path pairs. Full microbenchmarks use `Job.Default`
with the standard out-of-process CLI generator/builder/executor. Smoke uses in-process ShortRun with
three 100-ms measurements and is explicitly **not** gate evidence. The G8 script
reports measurements, not pass/fail timing or release verdicts; P1/P2 provide
those. A missing result or undefined interval is never turned into zero or pass.

The baseline side is required to be commit 7c45d47. LatestPatch roll-forward is
allowed only within each requested major/minor; the runtime actually selected
is recorded. No net10-to-net11 roll-forward replaces the original-runtime
baseline. The separately labelled attribution run belongs to P1.

## Cases and workload boundaries

- `CoreBench`, `BlockBench`, `TxBench`, `VmBench`, `NetworkHeaderBench` and
  `CorpusVmBench` retain the audit's named operations and setup. `direct` uses
  the same typed delegate/static result sink and five adaptive, warmed batches
  with `GC.GetAllocatedBytesForCurrentThread`; it never invokes reflection or
  boxes a result inside the measurement callback. Mux is excluded from this
  counter because its worker-thread allocations require the BDN diagnoser.
- `FieldReadBench.ReadEveryField` decodes all 30 blocks and recursively calls
  every concrete schema getter, including output unions, nullable body fields,
  witnesses, certificates, governance and metadata. Typed expression visitors
  are compiled in setup, not timed. Primitive and memory contents are consumed;
  opaque `CborAny` and CBOR-in-CBOR leaves are traversed token by token through
  **SAIB.Cbor**, not System.Formats.Cbor. `field-coverage.json` lists the concrete
  properties reached by these fixtures. Absent optional values cannot fabricate
  coverage of an absent union variant. Interface dispatch is the SDK's normal
  boxed-union path; ordinary concrete property results stay strongly typed.
- `EncodingBench.Raw*` copies the preserved original encodings. `Fresh*`
  recursively reconstructs the complete model using generated typed `Create`
  factories, re-encodes opaque CBOR leaves and materializes the final bytes.
  It never takes an original model's `Raw` shortcut. This is *reconstruction plus
  encoding*, not a writer-only benchmark: lazy structs store encoded field
  slices, so `Create` encodes each newly built node as its owned representation.
  The audit's shallower Conway-container rebuild remains a separate named row.
- `FixtureHashBench` hashes all fixture headers and transaction bodies with
  Blake2b-256 and -224. Signing/verification/address controls retain the audit
  inputs. Keys are generated in memory only; synthetic UTxOs are not real funds
  and no transaction is submitted. Fees use the pinned public protocol fixture.
- `ScriptBench` measures Flat decoding and predecoded evaluation separately for
  **each** of the 78 scripts. Evaluation includes fresh machine construction;
  the independent `VmBench.CreateMachine` row measures that construction alone.
  Corpus methods use `OperationsPerInvoke` normalization; a B/op or ns/op is per
  block, transaction, body, header or script as the case declares, not per set.
- Captured LSQ/rejection frames are an explicit placeholder. The report says
  **no baseline: path did not work at 7c45d47** and retains actual exceptions;
  no meaningless failed-decode timings are substituted. B2/B3's first successful
  path must establish a new baseline rather than compare against a failure.

## Fixed-work whole paths

N2C runs directly from WSL against the kept node's Unix socket, without a TCP
proxy. Unless `--node-already-running` is explicitly given, the script starts
`chrysalis-preprod-node`, checks readiness through that socket, and always stops
it with `docker stop -t 180` in a finally block. It never opens the database.
If the direct socket is unusable, it fails and stops; it does not add a proxy.

`range.json` pins an anchor and 1,000 consecutive successor blocks by slot,
header hash and SHA-256 of the full block bytes. It is captured once from the
node and reused by both sides, never silently refreshed. The anchor comes from
the first audit block. N2C measures full-block chain-sync; N2N measures sequential
headers plus one BlockFetch range over **the same successor blocks**. Handshake,
intersection, expectation/header-result array creation, a complete warmup and GC
are outside the fixed-work timer/counter. Blocks, protocol processing and
per-block SHA-256 verification into a stack span are inside. The hash adds the
same controlled validation work to both sides and allocates no managed output.
Allocation uses
`GC.GetTotalAllocatedBytes(true)` across all process threads, divided by the
fixed number of blocks, not the thread-local microbenchmark counter. The SDK lends some message bytes only until the next receive. Each lazy block
is therefore consumed and hashed immediately, never retained across receives
and never copied just to validate it later. Owned header points are checked
after timing. This lifetime rule also applies to any comparator.

BlockFetch is an N2N protocol and cannot be sent to the N2C Unix socket. With
`--n2n-container`, a short-lived official Microsoft .NET 10/11 runtime container
shares only the existing node container's network namespace, reaching
127.0.0.1:3001 directly. It mounts work/ for the harness, **never node/**, and
never changes or restarts the node container. Without that flag N2N is explicitly
not measured, not a pass. Container runtime/environment is retained in each
network result; it is not conflated with the WSL host's runtime.

## Fixtures, provenance and safety

`fixtures/` is the audit's 364 files copied byte-for-byte: 30 public Conway
blocks, 164 reconstructed transactions, 164 bodies, protocol parameters and
synthetic payloads, plus original manifests/discovery data. `sha256.json` adds
hashes for **all** copied files, the five public captured wire frames and all
78 existing repository Flat scripts. Every executable mode validates this
manifest before doing any work. Fixtures are not fetched or adapted at runtime.

The files are public chain blocks/transactions/bodies, public protocol values,
public discovery JSON, synthetic bytes and public captured protocol responses.
Transaction witnesses contain public verification keys/signatures, not private
signing keys. There are no mnemonics, private-key/encrypted-key files or secret
inputs in this fixture set. The harness never reads test/secrets/ or any user
wallet. Dumps are not needed and should remain disabled.

`audit-allocations.json` copies the 52 numeric .NET 10 five-batch audit rows,
including their exact/range distinction, original SDK/runtime/method and the
source document SHA-256. Differences must be explained, not hidden by widening
ranges or replacing the original-runtime baseline.

## Stable BenchmarkDotNet / .NET 11 adapter

The official NuGet index checked for G8 lists 0.15.8 as the latest stable BDN;
0.16.0 entries are prereleases. 0.15.8's runtime enum ends at .NET 10, and its
normal .NET 11 out-of-process path throws in `GetRuntimeVersion(NotRecognized)`.
`FrameworkToolchain` uses the supported `CoreRuntime.CreateForNewVersion` API
and BDN's **unchanged** `CsProjGenerator`, `DotNetCliBuilder` and
`DotNetCliExecutor`, selected by an explicit TFM and exact host runtime patch.
A plain public `Toolchain` wrapper replaces only the unsupported enum-based SDK
validator; the runner checks `dotnet --version` against each staged SDK pin
instead. Boilerplate builds are sequential (`DisableParallelBuild`) to avoid a
second enum-based check in BDN's parallel-build partitioner. A safe explicit job
ID avoids commas in BDN-generated MSBuild property paths. These choices are
identical on both sides and change no default measurement iterations, warmup,
calibration, overhead subtraction, GC policy, memory diagnoser or statistics.
No warning or optimization validator is suppressed. Remove this compatibility
adapter when a stable BDN release supports .NET 11 directly and revalidate both
sides before using the new version's measurements.

## Invocation isolation and review controls

`--output` must be new or empty. A nonempty directory is refused before builds or
measurements, including a previously successful invocation. Each invocation claims
an atomic `.invocation.lock`, generates a UUID, and catalogs only successful
producer artifacts. Summary inputs must match that UUID, side, source commit,
harness-source SHA256 and artifact-content SHA256. Unregistered, modified or stale
files are not measurement results. Keep old evidence in its original directory;
choose a fresh output path for the next run.

Direct counters now warm each selected case for `--direct-warmup-ms 1000` before
five adaptive batches. Each sample records its temperature and the result records
`steadyStateCertified: false`; this is not a quiet-machine or steady-state claim.
Use `--direct-warmup-ms 0` only for explicitly cold/transitional, audit-compatible
controls. Cold AddressEncode batches are not a regression verdict.

For offline runtime attribution only, add `--include-runtime11-attribution` (smoke
mode, no live network). This runs the exact same SDK10-built baseline DLL path
on runtime11 using `--fx-version`, verifies all Chrysalis assembly hashes, and
labels that side `baseline-runtime11` / `attributionOnly`. It does not rebuild the
baseline with SDK11 and never replaces baseline/runtime10 in a gate comparison.

`--measure replay --include-runtime11-attribution` is an offline receive-component
control: real Plexer/Demuxer/ChannelBuffer/protocol/block decode over memory pipes,
30 immutable fixtures repeated to 1020 blocks per batch, five process-wide GC
allocation batches in one process. It does not open a socket or start a node.
It excludes socket, request sending, node and BlockFetch agency; common producer
pipe overhead is included. It cannot by itself attribute live whole-path totals.

## Completion, source identity and local-only safety

Each invocation records RUNNING, COMPLETED, FAILED or INCOMPLETE. A failure is
written to the manifest before the single final summary render. FAILED appears
on the first Markdown line with the failure text; normal measurement collections
are empty, and retained raw artifacts are diagnostic only. Missing peer cases or
rejected artifacts make otherwise completed measurement output INCOMPLETE.
Unstated legacy completion is UNVERIFIED, not an assumed success.

`results.json` exposes `status`, `gateInputEligible`, `gateInputReason`,
`caseCoverage` and `rejectedArtifacts`. `gateInputEligible` is only a fail-closed
completeness check, never a performance pass or a substitute for P2's policies.
The Python `load_gate_input(path)` reader rejects failed/incomplete/unverified
results. The standalone summarizer exits nonzero unless completion is verified.
Coverage rows distinguish MISSING and REJECTED (with reasons) for micro, direct,
network and replay cases; absent samples are not invented as zero. Unregistered,
changed, missing or identity-mismatched artifact files are listed explicitly.

Both source trees have a `treeId`: `git read-tree HEAD`, `git add --all` and
`git write-tree` operate only through a fresh temporary `GIT_INDEX_FILE`.
The real index is never used for these writes. The content ID captures tracked
and unignored working files, including uncommitted/untracked benchmark sources;
ignored build outputs are intentionally excluded. Source IDs are captured before
building and checked again before measuring. Artifact identity also includes the
source tree ID. This complements, rather than replaces, compiled assembly and
shared-harness fingerprints.

The actual resolved BenchmarkDotNet package version is read from each built
`obj/project.assets.json`; all sides must agree on it and on the loaded BDN
assembly version before any validation or measurement. Each side's package
version is shown in the readable summary.

Network runs inspect the required runtime images locally before starting a node.
A missing/unavailable image fails clearly: the runner never pulls it. Every
runtime-container command also uses `--pull=never`, closing a removal race between
inspection and execution. This adds no Docker proxy and changes no node ownership.

Comparison eligibility additionally requires the named `baseline` **and**
`candidate` sides. A single-side diagnostic run (including baseline plus its
runtime11 attribution side) is `NOT_A_COMPARISON`, never an eligible gate input.
The reader rechecks actual side presence, so copying an old eligible JSON file
and deleting a side does not preserve eligibility.

All explicit git commands in the scripts set `GIT_OPTIONAL_LOCKS=0`, including
status and ignore-rule inspection; optional stat-cache refreshes cannot write the
real index. Output directories must be inside the repository **and git-ignored**.
This is checked before directory creation or builds, with an explicit output-
location error rather than a later “working tree changed” failure. Use a fresh
path such as `benchmarks/out/<new-run>`.

## Capturing a replay corpus

`corpus` records public N2N traffic, not performance. Use the 7c45d47 client:
call G8's `run_benchmarks.stage("baseline", source, output)` on a task-local copy
of the frozen baseline inputs, then build the staged harness with SDK 10.0.301,
net10.0 and `--no-dependencies`. Assert the eight product DLL hashes from the
pinned preparation record before and after; only the harness is rebuilt. Run
with `dotnet exec --fx-version 10.0.9 <baseline-harness.dll>`.
Capture and header recording are network I/O; they need no heavy slot.

The two argument forms (each output directory must be new) are:

```
corpus capture HOST PORT NEW_OUTPUT_DIRECTORY ANCHOR_JSON DECLARATION_MD
corpus headers HOST PORT NEW_OUTPUT_DIRECTORY RANGE_JSON
```

Before looking at any block or header in the range or its anchor, write a
range declaration: T0 is exactly 48 hours before that declaration's timestamp,
rounded down to a whole UTC hour. Record the UTC time, weekday and matching
mainnet slot. The range is the first block whose slot is at or after that slot,
plus the following 999 blocks; its anchor is the block immediately before it.
There is no content selection, redraw, candidate 2 or 24-hour wait.

The declaration Markdown contains these three fields (the timestamp is UTC):

```
- Declaration time: DECLARED_UTC
- T0 = T0_UTC (weekday)
- Mainnet slot at T0: FIRST_SLOT.
```

After writing the declaration, make **one** no-key Koios GET for the last block below the declared slot. Keep the
exact URL, UTC time and raw response in evidence. It supplies only the anchor,
never block content or another draw. If Koios fails, stop; do not use another
index. `ANCHOR_JSON` holds `anchor` in RangePoint form (`Slot`, `Hash`, `Sha256`
empty), `blockNumber` and `observedUtc` from that lookup. Capture refuses a
missing declaration, a declaration not before lookup, an anchor at/after the
boundary or a first returned header before it. It preserves `declaration.md`
for independent verification. The relay's tip must still be 3,160 blocks above
the anchor. IntersectNotFound stops the run and is reported, never retried as
an orphan candidate. Run `headers` on relay B with the same captured range.

A is `backbone.cardano.iog.io:3001` (IOG); B is
`backbone.mainnet.cardanofoundation.org:3001` (Cardano Foundation). Only
`backbone.mainnet.emurgornd.com:3001` (EMURGO) may replace a failed relay; keep
two different operators. One connection at a time, depth-one requests, no
probe. After failure: at most one retry per relay, at least ten minutes later,
then the fallback under the same limit, then stop. Do not automate retries.

Capture and headers each have a fixed 30-minute total deadline and a separate
30-second handshake bound. Every attempt
keeps its duplex streams and JSON record, even on failure. Errors log their
type, not local endpoints. Log UTC, load, duration and relay DNS names, never
the PC's address or host name. Do not print block bytes or metadata. Keep-alive
sends stay on the capture task; pending replies are awaited only after the
headers or BatchDone, not while a full block-fetch queue needs draining.

The independent verifier never writes its inputs. It is light Python and needs
no heavy slot. It accepts Python 3.9–3.12. For mainnet:

```
python3 benchmarks/scripts/corpus_verify.py --input STAGING --relay-b SECOND_RELAY --out NEW_CORPUS --manifest benchmarks/corpora/mainnet-ANCHOR_SLOT.json
```

`NEW_CORPUS` must end in `mainnet-ANCHOR_SLOT`. To import the pinned preprod
recording instead, use `--local-node --range benchmarks/Chrysalis.Benchmarks/range.json`
in place of `--relay-b`; its output name is `preprod-p1-n2n-recording-a`.
That mode reports the original local node 11.1.3, with no second operator and
no depth check. Every other verification check runs. Mainnet additionally checks the preserved
declaration, its mainnet time-to-slot conversion, and
`anchor slot < declared first slot <= first block slot`. The recorded handshake
must accept version 14 with the correct network magic. Every mainnet protocol
ends with MsgDone; the older preprod recording has no keep-alive traffic.
Source identity is the capture
`harnessSha256` and verifier `sha256`, not the parent repository commit. The output and manifest
must be new and outside both inputs; the manifest must be outside the corpus.

Keep corpora outside repositories. Set `CHRYSALIS_CORPORA` to their absolute
parent directory, with no default or search. The manifest pins `sha256.json`,
which pins every other file and excludes itself. The corpus id is the SHA-256
of the manifest bytes, printed in the verifier report and never stored inside
that manifest. `corpus.json` stores `headers` as offset/length pairs in the
inbound stream and `bodies` as groups of those pairs, including empty groups;
every block-fetch frame appears exactly once, in order. No CBOR is re-encoded.

Verifier controls require the pinned preprod corpus; missing input fails,
never skips: `heavy python3 -m unittest discover -s benchmarks/scripts -p 'test_*.py'`.
Run the harness `self-test` on the staged baseline and candidate too. It proves
the full-queue heartbeat case using real demux/block-fetch code and fixtures,
without a network connection. Header body-size counts are distinct from raw
block sizes and from messages spanning mux frames; shares are reported, not
acceptance criteria. Corpus bytes remain private to local/approved Fly
benchmark runs; redistribution requires the owner's decision.

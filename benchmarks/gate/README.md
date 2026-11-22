# Performance gate

On Linux, run `python3 benchmarks/scripts/gate.py run --mode change --baseline <commit>
--candidate <commit-or-working-tree> [--only <comma-separated-patterns> | --plan
<file>]`. `release` mode accepts commits only. The command builds immutable
source exports with one captured harness/client, then measures allocations on
this PC with one held `heavy` slot for staging and every measurement group,
recording load before and after every process. PC runs do not sample `/proc`;
measured-window CPU admission is Fly-only. The runner detects the machine
before stripping the caller's environment, never trusting a job's label.

Set `CHRYSALIS_GATE_LOCAL` to a private JSON file outside the repository. Required
keys: `corporaRoot`, `pinnedBaseline`, `flyOrg`, `flyTokenFile`, `flyLock`, and a finite nonnegative `flyBudgetLeftUsd`. The PC
command does not read a token or contact Fly. `corporaRoot` contains `range.json`
and `capture/`; `pinnedBaseline` contains the approved DLL closure and
`closure.json`. Never rebuild 7c45d47: stage its original DLLs read-only and
verify their hashes before and after measurement. Other sides use their own
`global.json` SDK, targeting net11.0. All restores use the local NuGet feed.

Wrap a benchmark that requires candidate-only APIs in `#if !CHRYSALIS_PINNED_HARNESS`.
No Azure paired row may select it, including through a timing wildcard.
Azure preparation checks both built arms' methods without running a measurement.

### Native MacBook Pro merge gate

Use `work/bin/mbp-alloc`, not `gate.py run`, for native Mac allocation. The
launcher always uses `mbp-run --memory-log`. The **admitted target commit**
provides the scoring code, bands, `Program.cs` instrumentation and fixed counts.
Each side provides its own benchmark bodies and project references. Put probes
in their own benchmark source files, not the instrumentation file. The native
`mac-bytes` mode is explicit and Darwin arm64 only. Default `direct` and timing
runs still use wall-clock calibration; fresh timing seals hash their sources as usual.

Materialize the approved merged source tree **unstaged** (the wrapper refuses
a staged index). Prepare a desk-owned `admission.json` outside the source tree:
`target`, `candidateCommit` and `candidateTree` are the full Git IDs in the
merge admission, `kind` is `direct`, and `rows` lists every required literal
`{"id":"CoreBench.Sign","kind":"direct"}` entry, in order. `pairs` defaults to
that row's target catalogue count; an explicit larger count is allowed, a
lower count is refused. Expand patterns against the target catalogue first.
An uncatalogued direct probe that already exists on the target runs paired under
strict D39. The built target's sealed probe inventory distinguishes it from an
actually absent probe. Only an absent direct probe may set `"firstBaseline":true`.
Its class needs a public parameterless `Setup()`, even if setup is empty.
It runs only on the candidate and reports absolute bytes per batch as `BASELINE`, not
a paired PASS. An uncapped first baseline exits nonzero and must be named as
accepted in the desk's merge evidence. An optional `"maxBytes":0` (or another
explicit value) is a desk cap, not PC parity; any batch above it gives FAIL.
Missing processes or batches fail closed; Mac batch IDs must be exactly 0 through
N−1 in order. An edit to the candidate's `Program.cs` is named in the report;
the target's instrumentation still runs on both sides. Obtain a Mac slot and choose a fresh
result ID, then run from the PC workspace:

```bash
work/bin/mbp-alloc run "$candidate_worktree" "$admission" "$id" "$mbp_root"
work/bin/mbp-alloc verify "$candidate_worktree" "$admission" "$fetched_packet"
```

Check both exit codes before committing; do not infer approval of an uncapped
first baseline. The wrapper prints the fetch path. `verify` checks the exact
row list/counts, side pins, each row's target band assignment and
runner assets, then independently re-derives the report from the fetched seal
and raw pairs. The same packet location keeps memory logs even when a setup or
measurement fails. Exit 1 means non-PASS direct evidence; 2 means a setup,
execution or evidence problem; the cap kills separately (137 for overflow, 70
if sampling fails). A sampled 9 GiB group RSS guard is softer than Linux
`MemoryMax` and does not cover unrelated work or detached processes. The Mac
runner removes staging/build scratch after producing the compact packet.

Replay is not part of this gating command. An opted-in network change uses a
separate `kind: replay` admission and a separate Mac job with its own evidence;
missing or failed replay is indicative and cannot override the direct job.
The fixed TCP endpoint is serialized across such jobs. No native timing
claim, Mac replay band, or cross-architecture absolute threshold is implied.

`--only` selects catalogue ids or simple wildcard patterns. `--plan` reads a JSON
array (or an `entries` array) of rows with `id`, `kind` (`direct`, `replay`,
`time`), `replay` where applicable, and `pairs`; `candidate` can override the
candidate for an entry. A new direct per-message row is D39. Time entries may declare `aPairs` and `pairs`
(B) in either mode; optional `chargeSeconds` raises, never lowers, the per-process
charge. Micro rows take that charge from `gated.json`'s `time.chargeSeconds` (Azure run 3's
measured projection × 1.5, never below 6 s). Explicit counts are sealed exactly, even below the validity minimum.
Default-set sizing alone may increase counts to use remaining capacity. An optional corpus
`manifest` is repository-relative; measurement awaits P4 MR-B's client support.
No ad-hoc row can silently obtain a noise band. An object with `entries` can set
`"sessionPerRow": true` to seal one Fly session per row, only in change mode
with exclusively recorded-only time rows. Only those entries may declare a Fly
`region`: it is sealed in the session, priced using Fly’s regional markup, and
checked on the launched machine. Omission keeps `iad` and its base price.

`band --side <commit> --set <comma-separated-patterns>` freezes baseline-only
10-pair bytes bands. D39 rows have no band. The six audit range rows share their original six-row
process filter, separately from D39 rows, even when selecting a subset. The
every-field range row runs alone. Bands apply only to that same composition;
new bands record the exact filter and warm-up in milliseconds. Immutable older
`direct` records mean 1000 ms; the 14 `*/warm10` records mean exactly10000 ms.
A band whose stored or immutable-window warm-up differs from the row is refused. Selected D39 filters are a union of simple patterns. For each
replay the long design runs on baseline runtimes 10 and 11; short and first-pass
runtime-11 bands come from the same short processes. Existing frozen selections are refused before staging or measurement. Frozen
files are never overwritten, and duplicate matching keys name both files and refuse. Keys include replay/row, design, window, runtime and corpus;
commit, raw-data hash, baseline DLL hashes and run seal identify provenance.

## Bytes rules

All seven range rows use a sealed fixed 10,000 ms warm-up before each row's
five measured batches, identically on both sides. The six-row process filter is
unchanged. The new window is `direct/warm10` (`readeveryfield/warm10` for the
standalone every-field row); old bands stay under their old keys and never match
this protocol. Per-batch JIT compiled-method counts bracket the allocation window
as diagnostics, not a gate. Rows with a nonzero direct band in their
current frozen window declare 16 pairs in both modes. The six warm10 zero-band
rows run together in one cohort at 3/5 pairs; ReadEveryField runs alone at 16.
Replay counts stay unchanged. An explicit measurement entry can still declare its count.

These implement point 55 and D23/D39, with D48's lower-modal extension.
- Direct D39: at least 3 independent pairs, 5 batches per process. A higher
  candidate modal or a wholly positive paired-difference interval fails. Equal
  or lower modal and upper bound <= 0 passes; lower is labelled `lower`.
  With equal modes, a mean difference rounding to zero at 0.1 B, and a CI
  containing zero with half-width < 0.1 B, the command runs only the needed
  baseline-against-baseline A/A controls, using the same harness, process
  filter, five batches and process-pair count. `equal (noise)` passes only if
  the control meets the same conditions, both nonzero half-widths are within
  a factor of ten, and every non-modal A/P batch deviation from the mode
  also occurs in A/A. Otherwise it is inconclusive with the reason. An
  ambiguous modal is inconclusive unless the CI already resolves an increase.
  No band can loosen D39.
- Range/replay change: upper bound <= frozen band passes; lower bound above
  band fails; otherwise inconclusive. A missing matching band is inconclusive.
- Range/replay release: upper bound <= band and lower bound <= 0 passes;
  a wholly positive interval fails; otherwise inconclusive. Long replay plateau
  must hold on both sides: only an adjacent or outer difference among measured
  groups 1–20, 21–40, 41–60 whose CI lies wholly outside +/-band establishes
  `no plateau`. A resolved increase fails before modal, band or plateau
  preconditions (change-mode band rows fail only above their band).
- Replays need at least 5 independent pairs. Change uses 1+25 passes, steady
  window 16–25, 20 pairs. Release steady uses 180+60, all 60, 10 pairs; its cold
  companion uses 1+25, 20 pairs, baseline DLLs on the candidate's runtime.
  Cold always means the first actual pass, `warmupRows[0]`, not the second
  actual pass in `rows[0]`. Corrected cold bands have versioned first-pass
  keys; the earlier second-pass files remain immutable history.
- A release FAIL alone gets one immediate repeat on this PC. Its original and
  repeat pairs are pooled; both separate verdicts and the joint interval remain
  visible. No repeat for inconclusive data, no second repeat, no discarded run.
- RECV1's item gate is stricter: u <= 0 B/block. The generic band's PASS is not
  that item verdict. The report always prints both interval bounds.

## Outcomes

`report <run-directory> [--mode change|release]` derives tables from that directory
alone, using its sealed rules, sides and bands, not the current repository.
Changed seals or raw artifacts are refused. Re-derivation in another mode is
labelled and never prints a release outcome. Precedence: FAIL, INCONCLUSIVE,
then SUBSET. RELEASE PASS requires release mode, canonical sides, the complete
release set and every declared row passing. Omitted rows by choice give SUBSET;
declared but missing rows give INCONCLUSIVE. Canonical sides are pinned 7c45d47
on 10.0.9 versus a net11.0 candidate on 11.0.0, or the previous alpha on 11.0.0.
The previous alpha is the latest rising `v2.Y.Z-alpha` version at a proper
ancestor of the candidate, never its own tag, an RC tag or an unrelated branch.
A SUBSET outcome line lists its reasons.
D51 requires both sync-path replay and other-hot-path times: PC bytes alone can
never give RELEASE PASS. `fly --go <seal-id>` runs only the sealed timing session after a separate go.

Only the designated release controls may prepare a retry. The coordinator's
`benchmarks/out/controls-authority.json` names the original seal and its gated
time rows with their sealed expectations. The original must have been sealed
with the same retry-capable launcher. Run `gate.py retry PARENT_DIR --kind
first|repeat` to print a new go without launching. A repeat-only retry reuses
the verified first payload and reserves one session; a first-phase retry
reserves first plus repeat. Under the Fly lock, the first launch rechecks the
original evidence and authority, then claims the lineage before consuming go.
A preflight refusal claims nothing; a claim left without a go marker still
blocks every sibling. Check that `time/runs`, if present, is a directory before
GO: a non-directory there can consume the retry without creating an app.
Allocation evidence remains in the original; a retry report cannot give a new
release pass.
Fly time never supplies a bytes verdict.

The runner is the single environment allowlist: ambient HOME/PATH/locale/time,
TMPDIR, DOTNET_ROOT and the non-secret Fly identity metadata FLY_APP_NAME,
FLY_REGION and FLY_MACHINE_ID. It also keeps `DOTNET_CLI_USE_MSBUILD_SERVER`,
`MSBUILDDISABLENODEREUSE` and `UseSharedCompilation` when set; staged restores
and builds always pass `-nodeReuse:false --disable-build-servers
-p:UseSharedCompilation=false`. Corpus, fixture/results paths, NuGet and .NET
telemetry/logo/TieredPGO are generated, never inherited. `P1_RANGE` is a generated
path for the current TCP client and goes away when P4 MR-B lands the manifest.
The client records no environment. `--observed` adds acknowledged measured-window
marks, outside timed passes; missing answers exit 3 within 30 seconds without a
result. Without it the client runs standalone, without waiting.

Keep the seal, raw `data/`, captured `input/` scripts and reports as evidence;
build outputs need not be copied. Bands committed to the repository contain no
machine paths or host names. See `../HOT-PATHS.md` for the baseline path count.

A report refuses an unsupported sealed rule version rather than applying a
new rule to old data. Cold companions use the resolved candidate's runtime;
a baseline built for a newer runtime may be unusable for a reverse comparison.

## Fly

For a change-mode sync-path time check, use one command to prepare both replays:

```sh
python3 benchmarks/scripts/gate.py run --mode change --baseline <commit> --candidate <tree-or-commit> --only 'time/preprod-*'
```

That command builds both sides, runs the bundle locally with 1+1 passes (not
counted timings), seals its inputs and prints the seal ID and reservation.
D70 fixes the change defaults at B34 TCP / B40 offline and approves a total
40 USD Fly budget. This session reserves about 62.4 minutes / 0.412 USD including
the lifetime reservation; preparation is not launch authorization. These defaults
do not change explicit entries, micro/mainnet counts or release A/B counts. D70
does not approve a different RC topology. After the
separate go for that exact ID, `gate.py fly --go <seal-id>` launches it. No go
is inferred from preparation. A consumed go cannot be used again. Re-report
with `gate.py report <run-directory>`; that reads only the sealed run directory.

The local JSON also needs `flyPythonRoot`, a relocatable Linux Python3.12 tree,
for local Fly bundle preparation. `flyBudgetLeftUsd` is required and is the remaining
approved balance, not the total budget; missing, invalid or insufficient values
fail closed before a launch. The public worker receives both preparation settings. Never put credentials there: `flyTokenFile` names the RAM file only.
The bundle copies the installed, explicitly selected .NET runtimes, the same
built DLL closures used on the PC, verified public fixtures and the offline
capture. All input files are hashed; there is no unpinned network bootstrap,
SDK build or NuGet restore on Fly. No token or private Fly configuration is
uploaded. The existing WireGuard peer is used; no private peer is created.

The sealed schedule has a homogeneous A prefix, then B (change mode has no A).
Two adjacent pairs have opposite orientations, ABBA or BAAB, balanced as nearly
as possible within each row and phase. Super-round placement spreads unequal
row counts over the phase. Odd counts retain a two-member tail; they are not
invalidated or rounded. A full four-member group, or two-member tail, starts only
when its whole charge fits before the existing cutoff.

The first 64 big-endian bits of SHA256 over sorted-key compact UTF-8 plan-core
JSON seed the schedule; there is no selectable nonce. The core includes the
rule, counts, build and input identities, not derived seed/schedule fields. The
host seal records core hash, seed, version and complete index. Worker plans and
output directories use fixed-width opaque IDs; build installation paths are the
same whichever role or slot uses them. Semantic side mapping stays on the host.
A repeat schedule is presealed; selecting failed rows only filters it and cannot
reseed or reorder survivors.

The exact canonical core JSON bytes are preserved with their hash and verified
before deriving the seed. One portable SHA256 draw stream uses domain
`chrysalis-timing-shuffle-v1\0`, an eight-byte big-endian seed and an eight-byte
big-endian counter starting at zero. A draw is the big-endian 256-bit digest;
rejection sampling removes modulo bias and consumes the counter even on a
rejection. Fisher–Yates swaps from the last index down to one. In each phase,
row IDs are visited in UTF-8 byte order: build the balanced orientation multiset,
draw an extra orientation if needed, shuffle, then draw an odd tail. An extra
or tail draw maps 0 to ABBA/AB and 1 to BAAB/BA. After these draws, each
super-round's canonical row list is shuffled from the same stream. Seed 0
shuffling 0–9 gives `[1,7,4,5,8,0,3,6,2,9]`; a whole-schedule golden fixture pins
the odd-group, tail and row-order draws. No platform-specific RNG is used.
New resource sizing grows counts by two; grandfathered explicit odd controls
retain their exact tail and the unchanged five-admitted-pair minimum.

This changes scheduling, not inference: admission rejects a pair, never its clean
neighbour; arithmetic process means, pair-ratio Student-t intervals, five-pair
minimums, arithmetic twenty-pass stability groups, quiescence, A-band and existing
noise-record eligibility are unchanged. Block log contrasts, orientation/slot,
predecessor builds and timestamps are information only. They cannot recenter a
result or turn an invalid prerequisite into a pass. Persistent side bias and
asymmetric carryover can remain; ABBA is not a promise to cancel them.

The first-five-minute steal report reuses existing whole-process before/after
counters, excluding host probe processes: select scheduled processes starting in
[t0,t0+300), where t0 is the first long scheduled comparison client start.
Report n, median, p95, observation span and whole-comparison median;
also show first-all-client context if a short prelude starts earlier. A selected
process can end after 300 seconds. No new probe, wait, threshold, rejection,
replacement or automatic retry is added. A valid interval straddling the change
tripwire goes to the main session unchanged; it is not automatically resealed.
A new seal can be authorized after a failed prerequisite (A band, stability,
minimum admitted pairs, identity/work/completeness or budget stop), not to wash
a valid straddle through. Every such seal still needs its own go.

The shared lock is held across the entire go, including follow-up sessions.
Other apps are neighbours, never blockers or cleanup targets; only their started
machine counts are reported as context. Any old benchmark/gate app refuses a
new launch. Each new app is recorded before creation. Machine identity, image,
region, performance-8x shape, one start, no restart/replacement, no services, IPs,
volumes or secrets and the held lock inode are checked twice. A mismatch makes
the session INCONCLUSIVE and forbids its repeat. Cleanup destroys only that
recorded app, retrying with backoff until its sealed deadline and an absence
proof. If proof still fails, the watchdog retains the lock and keeps retrying;
CLEANUP FAILED names the app and marks lifetime/cost outstanding. With no live
watchdog to accept ownership, the launcher itself retains the lock until proof.
Ctrl-C/SIGTERM on the public command forwards to the launcher and waits for its
cleanup; no parent kills the launcher midway through recovery.

A detached watchdog survives loss of the launcher's process group. The VM's
main process independently exits ten seconds before its sealed guard and is
configured to auto-destroy. The lifetime comes from the hash-verified archive,
not a mutable local plan. D24's independent hard ceiling in code is 7,200 s;
P2 sizing never exceeds the 7,000 s guard / 6,600 s measurement cutoff. A shorter
6,450 s session is supported too. The seal refuses an excessive lifetime, an
underpriced session or a budget without the full possible repeat reserved.
There are no admission-driven replacements or automatic inconclusive retries.

## Host probe

Before any timing block, 16 baseline-only offline preprod processes run 180 warm-up
and 60 measured passes. Keep only if the unrounded mean of their sample CVs is
at most 0.054 and every startup `GCGen0MaxBudget` matches the noise-record host's
16,777,216 bytes. Both CV16 and lag-1 are recorded. An observed budget mismatch
is `gen0-budget-mismatch`; a missing budget or invalid series stops the session.
The comparison's per-build GC totals, rates per measured pass, and observed
budgets are reported separately and never gate. A/A passes count toward baseline.

In iad a session attempts up to six draws. Its launch reservation is the full
machine-guard price plus five times the probe-only price ($0.69393833 in iad);
a repeat has its own reservation. After every draw, verify its archive, probe,
absence proof and booked cost. A rejected machine runs no timing blocks. Redraw
only if booked cost so far plus the next full guard fits the reservation. With
the fresh controls' 6,298 s guard, the reservation is $1.36545961 per session,
$2.73091922 for two, or $4.09637883 including one retry. This is not authority to run.
If cleanup or bookkeeping runs past the guard, booked cost can exceed the
reservation even if compute stopped in time; stop the lineage and report it.
If the VM ignores the stop and cannot be destroyed, compute is unbounded until
cleanup succeeds; the watchdog retains the lock until absence is proved.

## Release rule

Name every path with no working 7c45d47 baseline and record its first working
measurement, which becomes its baseline thereafter. Never compare with a failed
decode as though it were a valid baseline.

D41/D49 rule (i) gates steady time: the paired mean ratio is always printed;
PASS requires its 95% Student-t interval [L,U] to have L <=1.00 and U <=1.05.
A valid L >1.00 FAILs. Otherwise it is INCONCLUSIVE. Each replay gets its own
baseline A/A band below 5%, then fresh B pairs under the sealed local
ABBA/BAAB schedule, with A first and rows spread over super-rounds. All declared pairs must finish, at least five per required phase
must be admitted. For A, average the two members' stability contrasts within
each pair before taking the interval across pairs. For B, check each side
separately. Each population must be stable over the three 20-pass groups, and
its aggregate JIT compile-time share must be <=0.5%, including timing-rejected
complete processes. The counter starts after `MEASURED_START` is acknowledged.
Methods compiled are reported, not gated. After the data and admission checks,
the report names every failed qualification check and its figure and limit.
Admission uses
only the acknowledged measured window: aggregate/per-core steal <=1%/2% and
foreign CPU total/per-process <=5%/2% of one core.

A release session does not repeat unless every gated row qualified (a declared
smoke must be complete). A controls row may seal `expected` statuses for release
and/or change mode, or `"smoke"`. Controls check both modes and skip the repeat
if any declared result is contradicted. Other plans use only their sealed mode.

Known limit: each B side's own check is unchanged, but the whole release gate
accepted drift more often in 58 of 294 simulated B-side cases, 31 beyond the
limit. The largest increase was on RECV1-like offline replay, with +5.5% drift
on the B baseline alone: 1.60% versus 0.01%. Of 3,451 extra passes, the old gate
had flagged 2,979 for the counter window, 413 for checking the A sides separately,
and 59 for both. None came from changing the B check. These are model results.

Full mainnet+preprod timing fits one machine up to about 12.5 MB. Above that,
D51's option 2 uses two sessions, preprod TCP+mainnet offline then mainnet TCP+
preprod offline. Mainnet stays gated in full. Options 1 and 3 remain explicit
configuration choices, not automatic fallbacks; option 3's B-only mainnet rows
say fail-only and CLEAR, never PASS. Missing mainnet support/corpora cannot be
silently replaced by the preprod set. Mainnet manifest measurements join when
the manifest client and MR4's bands land.

The other hot paths (hashing, signing, transaction building/fees, Plutus,
encoding and node-to-client) run as the micro session. At the catalogue's micro
charges a release-mode micro set needs more than one session, and sealing refuses
it until main decides how to split it. The micro rows reuse
the direct harness's bindings and ReplayClient's pass counters/observed marks,
with one common invocation count fixed from a non-counted baseline sizing
probe (target10 ms/pass), then verified in a final1+1 dry run of both sides.
The count is sealed; there is never adaptive per-side calibration. Existing
micro data contains benchmark names and aggregate time, not member time shares;
no share is inferred or new measurement added for that informational question.
The first RC includes a separate baseline-only micro band session when no
frozen record exists; its valid records are written to `time/noise-records.json`
for review and freezing in `gated.json` before later RCs. Its full cost is
reserved too. No RELEASE PASS can omit either replays or micro hot paths.

### RC topology

D71 approves three serial replay sessions for the RC. This executable still has
full-fit/option2 only; the three-session topology needs a separate reviewed code
change before an RC seal. The reviewed count/validity/price source is the planning
evidence `DIAG-rev2.1.md`, SHA256
`1b2e4caf33954971bfd9afece73fa4452a87b1772b158714b95f90585306e104`.
It is not a measured mainnet or micro validity claim; no forecast table is copied
here. Real launches still require their exact reviewed seal and separate go.

Only FAILed release rows get one new-machine follow-up, immediately, with the
same counts and their own A bands. Both sessions must be valid. Their B ratios
are pooled, not winner-selected; the larger A band is reported. The first,
repeat and joint intervals are retained. An invalid repeat holds the RC.

Change mode uses B pairs only and the 5% tripwire (U <=1.05 passes, L >1.05
fails), with a sealed baseline-noise record for that machine class/design.
A missing record is INCONCLUSIVE. PC time and Fly bytes never gate. Warm-up,
absolute times, GC/JIT observations, neighbours, duration and cost are reported,
not gated. Only a release-mode run of canonical sides, the complete declared
release set and passing bytes AND time can print RELEASE PASS. Missing data
is INCONCLUSIVE; an otherwise passing partial/change-mode run is SUBSET.

A row declared in the catalogue's `recorded` mapping, or a new explicit entry
with `rowClass: recorded-only` and an `explanation` link, retains its measured
numbers and the label **harness artefact, explained, not gated**. It never changes
the outcome or triggers a repeat. A measurement plan cannot downgrade an
existing gated catalogue row. This preserves the old in-process N2C evidence
when its independently verified socket replacement becomes the gated row.
Fly sessions archive and report an allowlisted hardware fingerprint for
information only. It does not read environment variables.

FieldReadBench.ReadEveryField may drift within a process. Alternating the side
order balances between-process drift; run order is not evidence of stability.
Release time checks the three group intervals for each B side and for the
pair-averaged A phase, and holds
the RC if they drift. Direct release bytes retain all five batches, compare
process means against the frozen A/A band and reject a resolved increase; they
do not claim a direct-row plateau or select the last/lowest batch. Whole-path
bytes separately apply the amended plateau rule: only an interval wholly
outside +/-band says no plateau. Both rules keep the original data visible.

### Azure launcher

`gate.py go SEAL_ID` dispatches to the sealed provider; `fly --go` remains Fly-only.
For an Azure seal, inspect `gate.py go SEAL_ID --dry-run` before the go. It prints each full reservation and the number of attempts the current ledger guarantees; it starts nothing. An Azure seal needs the Linux/amd64 `mbp-container build` and PC import path. The seal and payload must carry the same approved container build marker; without the reviewed runner pin, the launcher will not start. The launcher does not prepare or measure clients on the PC.

The PC settings file named by `CHRYSALIS_GATE_LOCAL` binds four canonical paths: `azureCli`, `azureLock`, `azureBaseline`, `azureLedger`, plus the desk’s `azureRemainingProjectionUsd` estimate for the rest of the program. For Azure, the PC pins the Python mirror manifest digest as described below; `flyPythonRoot` is only a Mac build-guest setting. The ledger is mode 0600 and replaces per-phase spending counters. Its opening booking is $4.29 (Fly and Azure combined); before each go and each attempt the strict program ceiling is $40, and controls additionally stay below $6 in their original lineage. An unresolved attempt retains its full reservation; posted charges are a separate comparison, not budget credit. The batch and cap-stop reports distinguish program booked, unsettled, observed posted, computed headroom, and the desk estimate.

Each attempt has its own tagged resource group, user-assigned identity with Contributor on its own run group only, private storage, and outbound-only VM. `Standard_F8als_v7` uses `eastus2` with one fresh `eastus` capacity fallback for that same draw. Quota is not capacity. The keeper is a bounded Windows process only. The VM's own group-delete timer and Azure's platform shutdown are independent; the platform schedule is confirmed before guest init, never moved in place, and removed with the group. A launcher never registers a provider.

| Deadline | From the same epoch E |
|---|---:|
| Worker started / provisioning writes | 483 s / 720 s |
| Worker cutoff / verified copy | C / C + 200 s |
| WSL cleanup / guest timer | G − 70 s / G |
| Platform shutdown schedule | G rounded up to the next UTC minute |
| Escalation / reserve ends | G + 5 min / G + 15 min |

Before the keeper callback there are no ARM writes. The PC is the only deleter until the guest timer and delete permission are verified at init. A crash before a platform schedule exists (or a schedule that does not deallocate) can leave compute billing until recovery: around $11.82/day fixed, plus disk transactions. Conditional on the schedule firing and deallocating, the maximum compute before it fires is about $0.97, then around $0.20/day fixed until deletion. The earlier no-VM gap can leave the IPv4 charge around $0.12/day. A successful schedule readback is not evidence of deallocation. Keep both cleanup paths and the ledger-first resume check.

| Blob | PC capability | Guest capability | Expiry |
|---|---|---|---:|
| `payload` | create/write | read, sealed to the init payload key | 30 min |
| `suite` (if sealed) | create/write | read, sealed to the init payload key | 30 min |
| `results` | read | create/write, sealed to the init results key | 15 min |

All six capabilities are separate HTTPS-only blob SAS. Plaintext stays in memory or an anonymous pipe only, never argv, files, logs or the environment. Run Command protected settings carry ciphertext. The guest uses ephemeral private keys under root-only `/run`, and transfers only verified bounded archives. Partial results are diagnostics, not data for a verdict. Azure CPU steal is **not observable on Azure**; foreign-CPU checks and the probe still gate.

On a fresh installation only, the coordinator runs `gate_azure.py initialize-ledger` against the exact baseline. If a ledger is missing after use, restore the known ledger instead; a GO never creates one.

A consumed GO cannot authorize another attempt after its dispatch exits. On resume, before any other work, check the ledger. If an entry is active or cleanup failed, `gate_azure.py recover` refuses a held lock and otherwise recovers the exact owned group before checking the baseline. If cleanup was not proved before the reserve expired, ordinary GOs refuse until explicit coordinator recovery. While group creation is unconfirmed, a qualifying watchdog absence proof is provisional: the full reservation stays booked and the next GO must delete and prove absence again after the reserve boundary. Failed re-proof stops ordinary GOs. Do not replace the baseline fingerprint. `gate_azure.py cost GO_DIR` is read-only against Azure, checks exact inventory, and reads whole-subscription ActualCost at +1 and +3 days (retrying a 429 throttle up to twice after 60 s and 120 s; if still throttled, it refuses with exit 4 after recording the inventory and writes no posted cost, cost stop or `cost-d*.json`). Keep daily checks at most 24 hours apart through three days after the last run. Reports retain only run bookings/posted figures, aggregate totals for other rows (`otherRows`/`otherUsd`), and separate aggregate totals (`trialRows`/`trialUsd`) for the closed one-off trial (`chrysalis-probe-[0-9a-f]{16}-[1-4]` dated on or before `20261119`, capped at `$0.9725`); a charge outside run groups, the exact baseline and that capped trial window, posted exceeding booked, or posted exceeding expected by more than 20% stops the gate. Do not start another go while cleanup or a cost stop is unresolved.

### Azure preparation and batch review

Azure preparation has two steps. From the PC, run
`mbp-container <tree> build --request <tree-relative JSON>`. The request sets
`kind` to `prepare` and supplies `mode`, `baseline`, `candidate`, `sealKind`,
`toolingCommit`, and `toolingTree`. Use full commit and tree IDs. It can select
rows with `plan` (a tree-relative path and its raw `planSha256`) or `only`, and
can set `orderingProof`.
The Mac's disposable Linux/amd64 guests restore from the local package feed,
then build every arm with `--no-restore`. The Mac host verifies the result and
publishes `prepared/`, `build-files.json`, and `completion.json` together only
after the guests and their staging have been cleaned up.
For selected micro rows, the build guest lists benchmark methods for each arm
(`micro-probes`) and takes no counted measurements; noise prepares also run one uncounted micro rehearsal. The Mac
host and PC importer do not run the client. The Mac's `data/gate-build.json`
names the public inputs only. `linuxDotnetRoot` and `flyPythonRoot` each contain a `sha256.json`
mapping relative paths to SHA-256 digests. `pinnedBaseline` and `corporaRoot`
retain their existing manifests.

On the PC, pass the fetched folder
`work/mbp-results/<worktree>/<run-id>/mbp-container-<token>/prepared` to
`gate.py prepare --from`. It verifies the cleanup receipt and exact fetched
inventory; do not move the folder out of the wrapper's result directory. The PC settings named by
`CHRYSALIS_GATE_LOCAL` must independently pin `azureToolingCommit` and
`azureToolingTree` to the approved signed tooling commit reachable from the
local `release/2.0.0-alpha` branch and its exact tree. The PC compares the
receipt to that pair and rederives the requested rows, sides and policy from
the pinned Git blobs, including band files, benchmark fixtures, and host and
guest scripts. Files in the launcher's worktree do not supply these inputs. It
then checks the public inputs. The PC does not store the .NET or Python
mirrors: the matching Mac `sha256.json` bytes arrive as `input/mirrors/dotnet.json`
and `input/mirrors/python.json`. Pin their digests independently in the PC
settings as `linuxDotnetManifestSha256` and `flyPythonManifestSha256`. The PC
compares the bundled runtime member hashes against those verified inventories.
It also checks the pinned container build marker and the
Linux-x64 native selections in each packed executable's `.deps.json`, and the
bytes of the selected native files at their declared paths. This includes the
1.7.14 arm: its direct references retain the original Linux Capstone target.
It then binds the PC settings and checks the live ledger before writing the seal. This
does not launch a machine. On a later approved go, the first kept VM runs the
sealed dry work before any counted block. The PC rederives fixed work and the
common micro count from the returned raw evidence; repeats use the same values.
Set `orderingProof` in the request for the first seal of a changed worker/client
recipe. A `sealKind: "noise"` request uses a plan with `"noiseRecords": true`
and builds baseline-only sessions for missing machine-class records; the merged
records are `time/noise-records.json`. Noise prepares rehearse each micro session
(pass and 3 s fail cases, `counted: false`, platform `pc`, never imported or
sealed; worker scripts run on the build guest's Python rather than the payload's
python3.12): `rehearsal/` ships in `prepared/` (no later step compares two
prepares) and stays out of the seal.

Batch plans name `"areas"` from `gated.json`. `gate.py carry LEG1_DIR` lists
outside rows to include in leg 2, distinguishing movement from unresolved data.
`gate.py seal-diff REVIEWED NEW --approved ASTRA OPUS` permits one-seat review
only for a batch-leg seal whose changes meet the batch rules. Each approval is
JSON with `seat` (`astra` or `opus`), `verdict` (`APPROVED`), and `sealId`.

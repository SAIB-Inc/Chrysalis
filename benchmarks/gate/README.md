# Performance gate

Run `python3 benchmarks/scripts/gate.py run --mode change --baseline <commit>
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

`--only` selects catalogue ids or simple wildcard patterns. `--plan` reads a JSON
array (or an `entries` array) of rows with `id`, `kind` (`direct`, `replay`,
`time`), `replay` where applicable, and `pairs`; `candidate` can override the
candidate for an entry. A new direct per-message row is D39. Time entries may declare `aPairs` and `pairs`
(B) in either mode; optional `chargeSeconds` raises, never lowers, the per-process
charge. Explicit counts are sealed exactly, even below the validity minimum.
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
for bundle preparation. `flyBudgetLeftUsd` is required and is the remaining
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
counters: select processes starting in [t0,t0+300), where t0 is the first long
client start. Report n, median, p95, observation span and whole-session median;
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
and 59 for both. None came from changing the B check. With MR5b's host probe in front,
the model put that case at 0.20% per RECV1-like machine drawn (0.035% at the tighter probe
limit). Across the four-host offline mix it was 0.70% (0.66%) per measured row,
versus 0.34% (0.35%) for the old rule behind the same probe. These are model results.

Full mainnet+preprod timing fits one machine up to about 12.5 MB. Above that,
D51's option 2 uses two sessions, preprod TCP+mainnet offline then mainnet TCP+
preprod offline. Mainnet stays gated in full. Options 1 and 3 remain explicit
configuration choices, not automatic fallbacks; option 3's B-only mainnet rows
say fail-only and CLEAR, never PASS. Missing mainnet support/corpora cannot be
silently replaced by the preprod set. Mainnet manifest measurements join when
the manifest client and MR4's bands land.

The other hot paths (hashing, signing, transaction building/fees, Plutus,
encoding and node-to-client) run as the micro session on every RC. They reuse
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

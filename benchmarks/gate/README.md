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
keys: `corporaRoot`, `pinnedBaseline`, `flyOrg`, `flyTokenFile`, `flyLock`. The PC
command does not read a token or contact Fly. `corporaRoot` contains `range.json`
and `capture/`; `pinnedBaseline` contains the approved DLL closure and
`closure.json`. Never rebuild 7c45d47: stage its original DLLs read-only and
verify their hashes before and after measurement. Other sides use their own
`global.json` SDK, targeting net11.0. All restores use the local NuGet feed.

`--only` selects catalogue ids or simple wildcard patterns. `--plan` reads a JSON
array (or an `entries` array) of rows with `id`, `kind` (`direct`, `replay`,
`time`), `replay` where applicable, and `pairs`; `candidate` can override the
candidate for an entry. A new direct per-message row is D39. An optional corpus
`manifest` is repository-relative; measurement awaits P4 MR-B's client support.
No ad-hoc row can silently obtain a noise band.

`band --side <commit> --set <comma-separated-patterns>` freezes baseline-only
10-pair bytes bands. D39 rows have no band. The six audit range rows share their original six-row
process filter, separately from D39 rows, even when selecting a subset. The
every-field range row runs alone. Bands apply only to that same composition;
new bands record the exact filter. Selected D39 filters are a union of simple patterns. For each
replay the long design runs on baseline runtimes 10 and 11; short and first-pass
runtime-11 bands come from the same short processes. Existing frozen selections are refused before staging or measurement. Frozen
files are never overwritten, and duplicate matching keys name both files and refuse. Keys include replay/row, design, window, runtime and corpus;
commit, raw-data hash, baseline DLL hashes and run seal identify provenance.

## Bytes rules

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

## Release rule

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
never give RELEASE PASS. `fly --go <seal-id>` is reserved for MR3 and currently
refuses without creating any resource. Fly time never supplies a bytes verdict.

The runner is the single environment allowlist: ambient HOME/PATH/locale/time,
TMPDIR and DOTNET_ROOT only. Corpus, fixture/results paths, NuGet and .NET
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

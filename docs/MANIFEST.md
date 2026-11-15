# Release and capability manifest

Last recorded: **2026-11-16**. Upstream audit and live-capture snapshot: **2026-11-15**. This is the single release record for the node-to-client matrix, node-pinned upstream reference sets, upstream watch triggers, and pre-release checklist. Update it for **every** alpha/beta/stable release; retain earlier release rows so the previous alpha remains reproducible.

## 1. Release record and evidence boundaries

| SDK release / source | Node reference set | Evidence status |
|---|---|---|
| Historical `1.7.14-alpha`, commit `7c45d47c87c63520cea84212dbd01ecc8d372ac3` | A: node 11.1.3 | **Observed baseline**, one Preprod run on 2026-11-15, .NET SDK 10.0.301 / runtime 10.0.9. Live matrix facts below come from this run. |
| `2.0.0-alpha` candidate, not published | A: node 11.1.3 | Required release target. The captured corpus passes offline after the .NET 11/platform work; **candidate live and installed-package integration verification are pending**. |
| `2.0.0-alpha` candidate, not published | B: node 11.2.1 | Required prerelease/experimental target; **untested live**. No test success is inferred from source support or tag existence. |

The historical report tested the source checkout and, in **REPORT step 9**, the published **1.7.14-alpha NuGet packages**: the covered N2N14/N2C20 successes and failures matched the source run, including the five failing query helpers and three failing monitor operations. Funded transactions were not duplicated in that package comparison. This is not verification of the new locally packed 2.0.0-alpha integration consumer. The four confirmed transactions and node parameters/blocks/frames are public offline inputs; see the [capture provenance, identifiers and byte-integrity manifest](../src/Chrysalis.Network.Tests/Fixtures/Preprod/README.md). The **1,544 passing offline/unit cases at commit `ee09e2e2f8d1bdaf305852015847653a69ac600b` (`ee09e2e`)**, including 232 new capture/replay cases, do **not** promote any live capability from untested/failing to tested. Candidate package-installed integration, forced-version runs, and the release performance gate are still release work.

Release version: **pending** (all packages must share one version; `0.0.0-dev` remains the development placeholder). Release results attachment: **pending**. Previous `2.x` alpha: **none yet**; the first alpha must say so rather than invent a comparison release. Node 11.2.1 is an experimental/prerelease target, not a recommendation for mainnet. Node software version, N2C/N2N handshake version, wire era tag, and ledger protocol version are distinct numbers.

## 2. Node-to-client capability matrix

**T = tested successfully**, **F = failing in the observed run**, **U = untested**. Every cell is a specific SDK operation × forced/negotiated N2C version × node pin. T/F cells below describe the historical baseline, not a fresh candidate sign-off. No forced-version sweep occurred: V16–V20 were offered, and only **V20 (wire 32788)** was negotiated by Chrysalis. V16–V19 stay U even though offered; V21–V23 stay U because the SDK did not offer them. Wire version = `32768 + N2C version`.

Cardano-cli controls negotiated V22 on node 11.1.3 and corroborated era/has-tx replies; they are independent controls, **not SDK V22 coverage**. Offline tests of an inner query model do not establish that the complete live helper works. Negative-test success means a refusal/rejection is decoded and surfaced as the intended typed outcome, not that the node accepts bad input.

### Node A — 11.1.3, baseline observation on Preprod

| SDK query / operation | V16 | V17 | V18 | V19 | V20 | V21 | V22 | V23 | Scope / observed limitation |
|---|---|---|---|---|---|---|---|---|---|
| Handshake acceptance | U | U | U | U | T | U | U | U | Observed V20/magic 1, not a forced-version sweep. |
| Wrong-magic typed handshake refusal | U | U | U | U | U | U | U | U | No real refusal capture/run; synthetic offline vectors are not live evidence. |
| Recent-point chain-sync | U | U | U | U | T | U | U | U | FindIntersect, RollBackward and 3 Conway RollForwards; not genesis-to-tip coverage. |
| Local state query: Acquire | U | U | U | U | T | U | U | U | Fresh volatile-tip acquisition succeeds. |
| Local state query: Release | U | U | U | U | T | U | U | U | Fresh acquired state is released successfully. |
| Local state query: ledger tip | U | U | U | U | F | U | U | U | Inline Result term is wrongly treated as an encoded byte string. |
| Local state query: current era | U | U | U | U | F | U | U | U | Same Result envelope failure; raw reply is [4,6]. |
| Local state query: protocol parameters | U | U | U | U | F | U | U | U | Same envelope failure; inner model decodes offline, not a working live helper. |
| Local state query: UTxO by address | U | U | U | U | F | U | U | U | Same envelope failure; captured one-entry public UTxO result. |
| Local state query: UTxO by input (empty set) | U | U | U | U | F | U | U | U | Same envelope failure; captured result is an empty map. |
| Other/version-specific state queries | U | U | U | U | U | U | U | U | No live SDK query coverage beyond the five helpers above. |
| Local transaction submission: acceptance | U | U | U | U | T | U | U | U | Valid funded transfers/script spend accepted and included; 3 positive N2C captures. |
| Local transaction submission: rejection | U | U | U | U | F | U | U | U | Inline node rejection reason is wrongly decoded as an encoded byte string. |
| Transaction monitor: Acquire | U | U | U | U | T | U | U | U | Snapshot acquired at the node slot. |
| Transaction monitor: GetSizes | U | U | U | U | T | U | U | U | Captured size list fully readable. |
| Transaction monitor: NextTx (empty) | U | U | U | U | T | U | U | U | Empty-snapshot reply only. |
| Transaction monitor: NextTx (non-empty) | U | U | U | U | U | U | U | U | No held-transaction snapshot captured. |
| Transaction monitor: HasTx false | U | U | U | U | F | U | U | U | SDK sends a text id, not the CLI control's era-tagged 32-byte id. |
| Transaction monitor: HasTx true | U | U | U | U | U | U | U | U | No held transaction observed in a monitor snapshot. |
| Transaction monitor: GetMeasures | U | U | U | U | F | U | U | U | 40,000,000,000 step capacity overflows Int32 on materialization. |
| Transaction monitor: Release | U | U | U | U | F | U | U | U | Waits for a nonexistent response; cancellation ended the observed wait. |
| Version-gated request rejected before send | U | U | U | U | U | U | U | U | Negotiated-version enforcement was not shown live. |

Observed public protocol/chain facts: Preprod magic 1, Conway ledger protocol 11.0, 10 fully decoded N2N blocks / 64 transaction bodies, three captured N2C Conway RollForwards, four confirmed signed transactions. These are one-run samples, not full-era replay or conformance claims. The monitor measures reply's second array item is **txCount**, not a slot; the capture has txCount 0 and step capacity 40,000,000,000.

### Node B — 11.2.1, required target with no live SDK run yet

Source-pinned node/network support requires **N2C V23**; older-version rejection is expected from the released source, but **has not been executed by this SDK test matrix**. Therefore V16–V22 cells are U, not fabricated failed test runs. The current SDK's V16–V20-only default offer is a known compatibility gap, not a successful V23 negotiation.

| SDK query / operation | V16 | V17 | V18 | V19 | V20 | V21 | V22 | V23 | Scope / observed limitation |
|---|---|---|---|---|---|---|---|---|---|
| Handshake acceptance | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Wrong-magic typed handshake refusal | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Recent-point chain-sync | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Local state query: Acquire | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Local state query: Release | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Local state query: ledger tip | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Local state query: current era | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Local state query: protocol parameters | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Local state query: UTxO by address | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Local state query: UTxO by input (empty set) | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Other/version-specific state queries | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Local transaction submission: acceptance | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Local transaction submission: rejection | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Transaction monitor: Acquire | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Transaction monitor: GetSizes | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Transaction monitor: NextTx (empty) | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Transaction monitor: NextTx (non-empty) | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Transaction monitor: HasTx false | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Transaction monitor: HasTx true | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Transaction monitor: GetMeasures | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Transaction monitor: Release | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |
| Version-gated request rejected before send | U | U | U | U | U | U | U | U | No live SDK result recorded for this node pin. |

Before release, force each proposed V16–V23 version in turn against node 11.1.3 and V23 against node 11.2.1, exercising every relevant operation, including typed refusal/rejection and pre-send version checks. Remove a version from the default offer if it does not pass; record exclusions and results rather than treating an unexecuted cell as success. Keep empty/non-empty next-tx and false/true has-tx evidence separate. A mempool race is not evidence for either held-transaction case.

### Other baseline limitations that release notes must not erase

- Node-to-node V14 chain-sync/block-fetch/keep-alive was observed on 11.1.3. New N2N versions, transaction propagation and peer sharing were not demonstrated by that run.
- Ouroboros submission and Kupmios submission via Ogmios were observed. Kupmios parameter reads returned stale defaults; Blockfrost and Kupo data reads were untested. The four transaction builds used independent cardano-cli parameter/UTxO snapshots, not proof of working provider reads.
- One simple Plutus V3 script matched the node/CLI budget (500 memory / 64,100 steps). V1/V2 live spends, complex validators, many-redeemer fee stress, language/version gating and full builtin conformance were not shown by that sample.
- The historical script fee calculation floored separate components (32 instead of the node/CLI's 34 lovelace); a 44-lovelace base-fee overcount masked that difference in the one confirmed spend. Confirmation is not a fee-conformance sign-off.
- Capture tests do not establish safe handling of unknown era tags or successful fresh re-encoding. Record those results when the relevant release work is actually verified.

## 3. Node-pinned upstream reference sets

Use the dependency set of the **node being tested**, not the highest ledger/Plutus/network version found on a default branch. The package versions below are from the official node release dependency tables, cross-checked against immutable local upstream tags; full commit ids identify the tags. A reference pin is **not** a claim that Chrysalis independently passed ledger/Plutus conformance at that tag. Record those conformance results separately when run.

### Set A — node 11.1.3

Node tag [`11.1.3`](https://github.com/IntersectMBO/cardano-node/releases/tag/11.1.3), commit `938cba990357ae7c4b7f95c8f75dd9d31174bbeb`. Stable-node baseline reference; candidate live verification pending.

| Upstream component | Node-pinned package version / tag | Tag commit |
|---|---|---|
| cardano-ledger-core | `1.21.0.0` / [`cardano-ledger-core-1.21.0.0`](https://github.com/IntersectMBO/cardano-ledger/tree/cardano-ledger-core-1.21.0.0) | `f649f9751074d2ab3de033fc3912f29c9862c1f5` |
| cardano-ledger-conway | `1.23.0.0` / [`cardano-ledger-conway-1.23.0.0`](https://github.com/IntersectMBO/cardano-ledger/tree/cardano-ledger-conway-1.23.0.0) | `f649f9751074d2ab3de033fc3912f29c9862c1f5` |
| cardano-ledger-dijkstra | `0.3.0.0` / [`cardano-ledger-dijkstra-0.3.0.0`](https://github.com/IntersectMBO/cardano-ledger/tree/cardano-ledger-dijkstra-0.3.0.0) | `f649f9751074d2ab3de033fc3912f29c9862c1f5` |
| plutus-core | `1.70.0.0` / [`1.70.0.0`](https://github.com/IntersectMBO/plutus/tree/1.70.0.0) | `507cab8cf6192e5143f12be5b2d3005a2928dc6d` |
| plutus-ledger-api | `1.70.0.0` / [`1.70.0.0`](https://github.com/IntersectMBO/plutus/tree/1.70.0.0) | `507cab8cf6192e5143f12be5b2d3005a2928dc6d` |
| ouroboros-network | `1.2.0.0` / [`ouroboros-network-1.2.0.0`](https://github.com/IntersectMBO/ouroboros-network/tree/ouroboros-network-1.2.0.0) | `c45735a56c567fa977969173d18943bac6bb3821` |
| cardano-diffusion | `1.1.1.0` / [`cardano-diffusion-1.1.1.0`](https://github.com/IntersectMBO/ouroboros-network/tree/cardano-diffusion-1.1.1.0) | `c5117705f6d5f8ea57ce3e772b6f9c35bfdb9167` |
| ouroboros-consensus | `4.2.1.0` / [`release-ouroboros-consensus-4.2.1.0`](https://github.com/IntersectMBO/ouroboros-consensus/tree/release-ouroboros-consensus-4.2.1.0) | `82ecba329d7d054340bf707d44fe6e9ac27cec40` |

### Set B — node 11.2.1

Node tag [`11.2.1`](https://github.com/IntersectMBO/cardano-node/releases/tag/11.2.1), commit `90bc1cd9380a78a63091ff673f8c5693152c8f60`. Prerelease/experimental reference; candidate live verification pending.

| Upstream component | Node-pinned package version / tag | Tag commit |
|---|---|---|
| cardano-ledger-core | `1.23.0.0` / [`cardano-ledger-core-1.23.0.0`](https://github.com/IntersectMBO/cardano-ledger/tree/cardano-ledger-core-1.23.0.0) | `95b369053fd32422695a81fb7d5289d6dd35eee2` |
| cardano-ledger-conway | `1.24.0.1` / [`cardano-ledger-conway-1.24.0.1`](https://github.com/IntersectMBO/cardano-ledger/tree/cardano-ledger-conway-1.24.0.1) | `95b369053fd32422695a81fb7d5289d6dd35eee2` |
| cardano-ledger-dijkstra | `0.5.0.0` / [`cardano-ledger-dijkstra-0.5.0.0`](https://github.com/IntersectMBO/cardano-ledger/tree/cardano-ledger-dijkstra-0.5.0.0) | `95b369053fd32422695a81fb7d5289d6dd35eee2` |
| plutus-core | `1.71.0.0` / [`1.71.0.0`](https://github.com/IntersectMBO/plutus/tree/1.71.0.0) | `261766ad1d582bce8c86bad1af7a3b0e5355b21c` |
| plutus-ledger-api | `1.71.0.0` / [`1.71.0.0`](https://github.com/IntersectMBO/plutus/tree/1.71.0.0) | `261766ad1d582bce8c86bad1af7a3b0e5355b21c` |
| ouroboros-network | `2.0.0.0` / [`ouroboros-network-2.0.0.0`](https://github.com/IntersectMBO/ouroboros-network/tree/ouroboros-network-2.0.0.0) | `4646039f05eb809bd5252af17121644ec1dc0c2c` |
| cardano-diffusion | `1.3.0.1` / [`cardano-diffusion-1.3.0.1`](https://github.com/IntersectMBO/ouroboros-network/tree/cardano-diffusion-1.3.0.1) | `4646039f05eb809bd5252af17121644ec1dc0c2c` |
| ouroboros-consensus | `5.4.0.0` / [`release-ouroboros-consensus-5.4.0.0`](https://github.com/IntersectMBO/ouroboros-consensus/tree/release-ouroboros-consensus-5.4.0.0) | `808e400545729b1246ba4393c69326ec4a04a730` |

The 11.1.3 release table links the Plutus 1.70 package changelog at commit `83fb488e05046a3698d0a80013c5fa76be2c0cf2`, whereas tag `1.70.0.0` resolves to `507cab8cf6192e5143f12be5b2d3005a2928dc6d`. Their complete source trees are identical (`2629616112f325e62ebd1ba3f201e7f31a7a24aa`); the commit identities are not silently equated. Network 1.2 and diffusion 1.1.1 likewise have distinct package tags/commits and are recorded separately.

Neither set uses the audit's newer Plutus 1.72/default-branch snapshots as a substitute. Dijkstra package/version presence is not evidence of production hard-fork activation, final CDDL, or SDK support. Keep each release's node tag, dependency set and actual test results together; append a new set when a node pin changes.

## 4. Upstream watch list — triggers, not speculative implementation

This is the **2026-11-15 audit snapshot**, not a live feed. Recheck these sources at each release. A development commit, draft CDDL field, merged backport or future-runtime announcement alone does not meet a release trigger.

| Item | Snapshot / boundary | Event that turns it into work |
|---|---|---|
| N2C V24, queries 40 / 41 | `GetStakePoolState` / `GetLeiosCommittee` are pending; V24 is not a usable negotiated version in the audited node/network set. | A released target node's pinned network/consensus actually negotiates V24 and specifies/permits queries 40/41. Then add exact request/reply fixtures, version gates and forced-version integration cells. |
| N2N V16 / V17 | Meanings differ across snapshots: newer network V16 is outbound transaction-submission AppV2; experimental Peras moved to V17. Older V16-Peras descriptions are not the new contract. | A released target node's pinned network negotiates **V16 and/or V17** with the corresponding protocol semantics. Inspect the pinned codecs/flow-control limits, then add handlers and interoperability/performance evidence; do not infer parity from version constants alone. |
| Leios / Peras | Experimental consensus, block/header and certificate plumbing is not proof of enacted network behavior. | A released target node/ledger/consensus pin supplies the supported wire/CDDL and activation/test configuration plus reproducible fixtures. Review scope and implement/test that pin, not an unreleased prototype. |
| Reference-input fee | Parameters in Dijkstra (including keys 55/56) are not by themselves an implemented charging formula. | The selected released ledger/node pin implements and gates the fee, with node/ledger oracle vectors. Only then add charging; never invent a formula from parameter names. |
| Plutus batch 7 | Default-branch PV12 gates/new costs are not proof that node 11.1.3 or 11.2.1 enables them; use each node's Plutus pin above. | A released target node pin enables the batch for specified ledger protocol/language/UPLC versions and supplies the corresponding costs/limits. Add node-pinned conformance and exact budget outcomes before claiming support. |
| Dijkstra hard-fork CDDL drift | The pinned experimental Dijkstra shapes and later unreleased CDDL changes are different reference sets; no production activation date is inferred. | A released target node/ledger tag or official hard-fork CDDL revision supersedes the selected pin. Compare the exact block/header, mempool-vs-block transaction, witness and parameter schemas and replay across the boundary before updating support. This watch does not replace work already planned against set B. |
| Roslyn pre-compilation generator output | `RegisterPreCompilationSourceOutput` is experimental in Roslyn 5.12 (`RSEXPERIMENTAL007`); its output visibility could serve the blueprint/serializer generator chain, but it is not shipped here. | The API becomes **stable in a released Roslyn version that the pinned SDK actually carries**, with the experimental restriction removed. Then evaluate deterministic cross-generator visibility and packed-consumer compatibility before adoption; a NuGet-only upgrade or warning suppression is not the trigger. |
| .NET runtime async | **Owner decision on 2026-11-15: no switch in this update.** All packages retain compiler-generated async; keep no second build variant/feature switch/CI matrix for runtime async. | Re-examine **only when a released .NET runtime contains fixes for the defects open at the decision**: runtime issues #137356 (live local), #136000 (GC write barrier; backport #136166), #137192 (native leaks; backport #137437). Recheck Mono/mobile/WASM support too. A disposable-branch probe and network tests/benchmarks under the normal performance gate may inform a new owner decision; a merge or an assumed .NET 12 default is not permission to switch. |

Watch references: the selected [network version source](https://github.com/IntersectMBO/ouroboros-network/tree/4646039f05eb809bd5252af17121644ec1dc0c2c/cardano-diffusion/api/lib/Cardano/Network), [consensus queries](https://github.com/IntersectMBO/ouroboros-consensus/tree/8a2a760c3fa13f0f8497c33e65a788af228e2a44), [ledger audit snapshot](https://github.com/IntersectMBO/cardano-ledger/tree/1d78754a701073420ab1b36a315d487cbdba9153), and [Plutus audit snapshot](https://github.com/IntersectMBO/plutus/tree/52fc5c0a111f9ae70fae22728bfc46458f63c5ca) are dated reference points, **not upgraded release pins**. Runtime defect/release status must be checked in the official dotnet/runtime issue and release records when its trigger is evaluated.

## 5. Mandatory pre-release checklist

The [repository release checklist](../CONTRIBUTING.md#release-checklist) requires this manifest to be updated before a release tag is created. This is a release gate, not authorization to publish. All boxes below remain open for `2.0.0-alpha` until the corresponding evidence exists.

- [ ] Select **one shared package version** and exact candidate commit; record the release tag/version mapping. Start with **2.0.0-alpha**. After it, every alpha takes its own rising **2.x** number: the **next minor** for a feature release (patch reset to zero), the **next patch** for a fix. The stable takes the **next minor after the last alpha**, without a suffix (for example, last alpha `2.3.x-alpha` → stable `2.4.0`). **`2.0.0-alpha.N` is not used.** Keep the development placeholder; supply the release version through the existing pack/tag mechanism rather than hand-editing different package versions.
- [ ] Build with the pinned SDK/target floor, zero warnings/errors, and pass all baseline cases plus reviewed additions. Align release tooling with that floor before cutting a release; documentation alone does not update CI.
- [ ] Pack every published package to a **local folder** at that exact shared version; record package ids, versions, dependency closure and hashes. Do not run a package-publish step to obtain this evidence.
- [ ] Install those exact local-folder packages into **Chrysalis.IntegrationTests** using an isolated restore/cache. Verify local package provenance and exercise the installed consumer, not production ProjectReferences; record generator/umbrella/package-closure results. Until that integration project/gate exists, this box cannot be completed.
- [ ] Run the candidate's forced-version/operation matrix against both required node pins. Record the date, network/config, negotiated version, query/operation, typed negative outcomes and result for every cell. Preserve tested/untested/failing distinctions and document excluded versions.
- [ ] Attach the **P2 performance-gate output** against **7c45d47 on .NET 10** and against the **previous alpha**, with the candidate on .NET 11. For the first alpha explicitly record that no previous alpha exists. Use fixed fixtures/work; include allocations, time, noise/tolerance, hardware/OS, SDK and both runtime versions. Apply all of the release rules below, not only a mean/tolerance comparison:
  - Time uses the **95% confidence interval of the paired mean candidate/baseline ratio**. A release passes only when its upper bound is within the proven measurement tolerance and the interval is **not wholly above 1.00**; a clear slowdown fails even inside tolerance. Deterministic steady-state allocations permit **no increase**, and zero remains zero. The upper confidence bound of whole-path allocation increase must be within the fixed-work, absolute bytes-per-block noise band frozen from baseline-vs-baseline runs; an increase whose interval is wholly above zero fails a release.
  - A benchmark that fails the release on its interval has **one allowed repeat run, never more than one**, on a quiet machine with load recorded. Recompute the interval over the **paired measurements of both runs together** and apply the same release rule. **Keep and report both runs**, whichever way the combined verdict ends; do not search for a passing run.
  - **No hot-path benchmark touched by this release may be listed as not gating or inconclusive.** Missing/noisy/undefined results are not passes; repair their measurement before cutting the release.
  - **Name every path with no working 7c45d47 baseline**, such as the previously broken query helpers, and record its **first working measurement**, which becomes its baseline thereafter. Never present a comparison with a failed decode as a valid baseline.
  - Take **timings on the MacBook Pro**, with the **Main PC as the x64 check**; **gate allocations on both**. Identify each machine and report the two runtimes/settings rather than combining unrelated-machine results.
  - Report the matched-work **Pallas comparison ratio with every gate run**, together with comparator versions/settings. It is **information, with no pass mark**, and never substitutes for the Chrysalis-versus-baseline verdict.
- [ ] Record exact **NuGet dependency versions** and restore sources alongside the **node/ledger/Plutus/network/consensus tags and commits** actually used. Do not replace a node-pinned set with a newer default branch. Keep compiler-generated async; no release depends on runtime async.
- [ ] Update this file's **release rows, upstream pin sets, capability matrix, watch list and remote-only acceptance register** from those actual candidate results; retain historical evidence rather than relabelling it as a new run.
- [ ] Attach one versioned **results file** and its referenced test/consumer/performance artifacts to the release evidence; fill the pending results location in section 1. The file must identify commit/package hashes, node pins, platform/runtimes, test/matrix outcomes, performance comparisons and dependency versions. A passing unit count is not a substitute for missing live/package/performance results.
- [ ] Prepare release notes listing every **known limitation, failing/untested operation, provider gap, experimental scope and unavailable live case**. A recorded response or source inspection cannot erase an untested-live label.
- [ ] Obtain the owner's explicit word before **any push, tag push, GitHub release creation or NuGet publication**. Until then all work, packs and results stay local. The existing tag-triggered publishing workflow is unchanged by this manifest; do not trigger it merely because local checks passed.

## 6. Waiting for the owner's word to push

Some acceptances can only be shown by GitHub or NuGet. Each item first supplies its local implementation and evidence; **only after that local acceptance is complete** is its two-part record **`accepted locally / waiting for the owner's word`**, not fully accepted. The register below lists the required deferred records; it does **not** claim that an item whose local work is still pending has already passed. Attach that item's actual local run when completed. These deferred remote checks hold no other local work and are the **first checks run after the owner's word**, with owner-configured Trusted Publishing where required. Replace the waiting record with the real remote run/publication result, never with an inferred pass.

| Remote-only acceptance | Required two-part record after local proof | Evidence to obtain after the owner's word |
|---|---|---|
| H1 — dependency-only pull-request run | accepted locally / waiting for the owner's word | A PR changing only `Directory.Packages.props` triggers the actual build/test workflow; attach the GitHub run. |
| H1 — Windows unit tier | accepted locally / waiting for the owner's word | Successful Windows unit/discovery tier in GitHub Actions, not the Linux local proxy. |
| H1 — macOS unit tier | accepted locally / waiting for the owner's word | Successful macOS unit/discovery tier in GitHub Actions. |
| K3 — first workflow run | accepted locally / waiting for the owner's word | First GitHub runs of the changed, commit-pinned workflows under their permitted events; local command execution is not an Actions-run result. |
| K4 — migrated test CI | accepted locally / waiting for the owner's word | GitHub execution of the xUnit v3/MTP jobs and filters in Chrysalis and both SAIB repositories, preserving discovery, integration selection and coverage. |
| G3 — devnet integration CI | accepted locally / waiting for the owner's word | The clean-machine devnet command completes in GitHub Actions for the required node matrix. |
| G5b — scheduled read-only sync | accepted locally / waiting for the owner's word | The real scheduled/nightly job runs and retains network/point/error/raw-byte diagnostics on decode failure; a local/manual run alone is not the schedule proof. |
| H4 — failed-CI publication block | accepted locally / waiting for the owner's word | A tag on a failed-CI commit publishes nothing; retain the GitHub release-job evidence. |
| H4 — Trusted Publishing / keyless publication | accepted locally / waiting for the owner's word | With the owner's NuGet Trusted Publishing configuration, a green-CI tag publishes without a stored key; retain the Actions run and NuGet publication/provenance. |
| H5 — stable-gate CI links | accepted locally / waiting for the owner's word | Replace the local proxy links in the stable checklist with actual successful CI run links and complete the remote stable acceptance record. |
| I1 — documentation samples in CI | accepted locally / waiting for the owner's word | The one-command documentation sample compilation runs successfully in CI; attach its GitHub result. |

# DijkstraNet

Captured on 2026-11-17 from a local cardano-node 11.2.1 synced to public
DijkstraNet, network magic 6. `manifest.json` pins every retained byte by SHA-256;
`../SOURCES.json` pins that manifest. The manifest names the exact official
Intersect image digest, consensus revision, genesis files and CLI producer.
No implementation code or private credential is included.

## Fork

The window keeps every block at heights 64737–64740, plus its raw header and
N2N ChainSync message. Heights 64737–64738 are Conway; 64739–64740 are Dijkstra.
Each full-block header equals the corresponding N2N header; consecutive heights
and previous-header hashes link all four blocks. Earlier headers were traversed
only to discover this already-final window; no future-block wait is needed.

Consensus revision `808e400545729b1246ba4393c69326ec4a04a730` defines header sum
indices in `Cardano/Block.hs:245-257,286-293` and serialises them through
`HardFork/Combinator/Serialisation/SerialiseNodeToNode.hs:125-148`.
`Cardano/Node.hs:158-204` defines full-block tags separately: two Byron tags,
then Shelley 2 through Conway 7 and Dijkstra 8. The observed header tags are
[6,6,7,7]; the observed full-block tags are [7,7,8,8].

The official book's epoch-14 / 2026-11-01 fork description does not match these
bytes. The last Conway slot here is 1295959 and the first Dijkstra slot is
1296023. With the pinned genesis (2026-10-18 UTC start, 86400-slot epochs), these
straddle 2026-11-02 00:00 UTC and epochs 14→15. The fixture records the observed
slots, not a hard-coded epoch taken from that description.

## Conway submission

One Chrysalis-built Conway self-transfer was submitted to the synced Dijkstra
node using wire era 6. The node returned AcceptTx (`8101`); the same-node CLI
then observed its two outputs at the original address. The manifest records the
public transaction id, 2,000,000-lovelace transfer and 168,493-lovelace fee.
`utxos-before.json` and `utxos-after.json` retain the independent node results.
The signing file stayed on the PC; only its test process read it in place.

## Replay

`B1GoldenProvenanceTests.DijkstraGoldensPinBothSidesOfTheForkAndTheAcceptedConwayTransaction`
replays these bytes without a node. All probes and offline golden replays have
Category=Integration, so normal offline suites are unchanged. The read-only runner is
`scripts/integration/run.sh --dijkstra`; it needs no wallet. The separate
`--dijkstra --funded` lane requires explicit original signing-file configuration.
Both need a separately provisioned local node. Never rerun a funded capture:
its CreateNew receipt has the fixed repository-local path
`.integration-artifacts/dijkstra/funded-self-transfer-receipt.json`, independent
of the capture output directory.

The committed Golden accepted-submission record is the cross-checkout guard; the ignored receipt guards concurrent or uncertain attempts.

## Configuration attribution

The copied configuration and genesis files come from
`input-output-hk/cardano-playground` under Apache-2.0; LICENSE and NOTICE are
retained here. No matching public commit was found for the served file set,
so the pin is its 2026-11-17 fetch date plus each served file's SHA-256. The
manifest and SOURCES distinguish copied configuration from public node outputs.
`python3 test/devnet/pin-dijkstra.py` rebuilds the manifest's derived hashes and
attribution from these retained public fixtures without a node or wallet.

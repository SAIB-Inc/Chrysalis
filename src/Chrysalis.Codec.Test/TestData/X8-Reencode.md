# Fresh re-encode fixtures

The X8 tests rebuild each supported fixture bottom-up through the codec factories, then
fully decode and compare the rebuilt value. Bytes declared opaque (`CborAny` or the
payload of `CborEncodedValue`) stay opaque; their enclosing records are still rebuilt.
A deliberately rejected, deferred or out-of-profile row is an inventory row, not a pass.

## Run

The normal offline suite includes `FreshReencodeTests` local rows and its controls.
From the Mac desk checkout after the Release build, run (substitute `<MAC_ROOT>`):

```sh
project=src/Chrysalis.Codec.Test
export CHRYSALIS_X8_PALLAS="<MAC_ROOT>/home/.cache/chrysalis/corpora/pallas-daecef59a1979d89515524d9583cc28655af182c"
export CHRYSALIS_X8_MAINNET="<MAC_ROOT>/data/corpora/mainnet-203130896"
/usr/bin/python3 "$project/TestData/FetchX8Pallas.py" "$CHRYSALIS_X8_PALLAS"
dotnet test "$project" -c Release --no-build --no-restore --filter Corpus=CodecReencode
```

Only the Pallas staging command contacts the network. It fetches the archive if needed,
verifies all hashes (including a concurrent publisher's cache) and publishes atomically.
A partial or altered cache fails. Keep the shared runner cache in the writable home path
above, or point both stage and replay at a provisioned Pallas cache under
`<MAC_ROOT>/data/corpora`; no permission change is needed. Both
replays are offline and independently verify their pinned inputs. `Category=Integration`
excludes them from the default offline and x64 suites; it does not indicate a live test.
The same selector requires both inputs. Mainnet uses the existing read-only 1,000-block capture (`corpus.json` and
`server-to-client.bin`), not a new node connection. Its inventory is capture order,
`mainnet/0000` through `mainnet/0999`, not chain slot numbers.

## Sources and disposition

- `x8-local.tsv`: all 21 checked-in Cardano block/header/transaction files, the 14 G1
  Preprod blocks and transactions, six cardano-ledger era transaction goldens and
  16 ouroboros-consensus disk and node-to-node block goldens. The source pins and
  relative paths are in the manifest. Consensus goldens are binary CBOR; source rows
  owned by the Byron fix retain their upstream hashes but are not vendored. Upstream
  licenses and notices are shared in `X7Attribution/` and indexed in `THIRD-PARTY-NOTICES.md`.
- `pallas-x8.tsv`: 94 pre-Dijkstra block/header/transaction candidates from Pallas 1.5.0-rc.2,
  including two extreme-depth X40 handoffs. The 15 byte-identical local mirrors are
  inventoried and hash-verified during staging, but not replayed twice. The Shelley/Allegra/Mary standalone Pallas
  transactions with a four-field minted envelope are tested in that envelope, not as
  the three-field native transactions (the ledger fixtures test the latter).
- Pallas exclusions: prototype Dijkstra/Leios and U5C, disk/index chunks and injection
  or inconsistent-index samples, genesis JSON, documentation, standalone scripts and
  loose sub-fragments. `babbage1.fr` is a Hydra vector of outputs, not a complete block,
  header or transaction. The `alonzo9.datums` and `.native` data belong to the source
  block's extracted fields, not additional ledger envelopes. `proposal-param-change-key*.hex`
  governance field fragments are not complete envelopes.
- The existing `X7PallasGoldens.json` and `X7CliGoldens.json` provide field views, source
  digests and deliberately duplicate-key probes; they are not additional production
  envelopes. The source Alonzo duplicate-key block, ledger Conway transaction and G1
  script transaction are included here. `X30WireForms.json`, `X32WireForms.json` and
  `unknown-era-manifest.json` belong to their dedicated wire-form and unknown-era
  tests, not the supported ledger-envelope fresh writer oracle. Those include malformed,
  synthetic or explicitly unknown forms. G1 headers/handshake, mux and other network
  messages are not block/transaction payloads for this guard.
- The `disposition` column must say `replay` for an active row. Other rows name their
  fix owner, X38/X40 handoff or out-of-profile reason. `x8-mainnet-deferred.tsv` identifies
  the single deferred block by original wire SHA-256 as well as capture ordinal.
  Replay validates the closed local and Pallas source inventories (file, decoder and
  digest) and pins their active counts before filtering; non-active rows must name an
  owner declared in `x8-local.tsv` or a verified local mirror.

## Byte differences

`x8-differences.tsv` identifies every accepted difference by original CBOR SHA-256,
fresh CBOR SHA-256 and the exact set of independently verified classes. For OC's
node-to-node goldens, these are the inner CBOR hashes; the tag-24 framing is checked
separately. Hashes of source files (including hex text or a tag-24 wrapper) are checked
by the source manifest. Unknown transformations, stale pins and new byte differences
fail. The only admitted differences are:

- `map-order`: relocation of unique, non-composite keys while preserving their matched
  values (RFC 8949 §§3.1, 5.6 and §4.2.1 for the *additional* deterministic-order rule).
  Duplicates and composite keys cannot be reordered by the classifier. Pinned ledger
  Alonzo/Babbage/Conway transaction body goldens put collateral (13) before outputs
  (1); the pinned ledger `TxBody.hs` encoders specify this order. Chrysalis's writer
  puts outputs first, following its property order. This is a writer preference
  candidate, not a claim of matching the reference encoder.
- `array-framing`: `pallas/alonzo22.block` has an indefinite first transaction input
  (`[hash32, 0]`, Alonzo CDDL line 84). The fresh definite array matches the pinned
  ledger's `Cardano/Ledger/TxIn.hs:124–128` (`encodeListLen 2`), differing only by
  changing byte 1012 from `9f` to `82` and removing the break at byte 1048.
- `map-framing`: definite versus indefinite map headers and matching break marker
  (RFC 8949 §3.2.2, also allowing the array framing above), with all nested wire items
  equal or independently classified.
  For example, `pallas/alonzo15.block` has a 200-entry MIR reward map at offset 1108.
  Pinned ledger `Shelley/TxCert.hs:356–376` uses the ordinary map encoder for this
  field. It emits indefinite framing at this size (`Binary/Encoding/Encoder.hs:397–443`);
  Chrysalis emits definite framing. This is a writer fix-or-waiver candidate.

- `argument-width`: the six mainnet blocks identified by this class keep the same
  definite chunks in the same indefinite byte string. Only the six-byte chunks’
  length headers shorten from `58 06` to `46` (three per block). RFC 8949 §§3.1,
  3.2.3 and 4.1 allow the original and specify the shorter preferred form. Conway
  CDDL 350–360 allows ≤64 bytes per chunk; ledger Plutus Data uses the standard
  byte-string encoder on each chunk. The on-chain form is accepted; the minimal
  fresh header agrees with the ledger's encoder. No other argument-width change
  is admitted.

This is a codec regression check, not permission to replace signed or hashed ledger
bytes with an equivalent CBOR encoding. It does not authorize changes to tags,
byte-string re-chunking, integer widths, duplicate-key occurrence order or nullable slots.

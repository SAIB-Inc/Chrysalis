# UTxO RPC tests

Offline: `dotnet test --filter 'Category!=Integration'`. Every golden has a SHA-256
and provenance line in `Golden/manifest.tsv`; missing or changed files fail.

## Live

All live tests are opt-in, tagged `Category=Integration`, and serialized in the
`Live` collection. Without the required variables, discovery reports a skip.
Only preprod is allowed. Never put a key value on the command line or in a log.

| Variable | Value |
|---|---|
| `CHRYSALIS_UTXORPC_ENDPOINT` | `http://127.0.0.1:50061` (no built-in default) |
| `CHRYSALIS_UTXORPC_NETWORK` | `Preprod` |
| `CHRYSALIS_UTXORPC_HEADERS` | Optional `name=value;name=value`; split at the first `=` |
| `CHRYSALIS_TEST_WALLET_ADDRESS` | Public address, or comma-separated addresses for parity |
| `CHRYSALIS_TEST_BLOCKFROST_KEY_FILE` | `<path>`; read only inside the test process |
| `CHRYSALIS_TEST_WALLET_KEY_FILE` | `<path>`; read only inside the funded test process |
| `CHRYSALIS_TEST_FEE_LEDGER_FILE` | Ledger path; required with the wallet key file |
| `CHRYSALIS_TEST_FEE_BUDGET_LOVELACE` | `5000000` for the whole task, not per execution |
| `CHRYSALIS_UTXORPC_RECORD_DIR` | Recorder destination; unset in acceptance runs |
| `CHRYSALIS_UTXORPC_RECORD_ADDRESS` | Public third-party address; never the wallet |
| `CHRYSALIS_UTXORPC_RECORD_TX` | Recent public transaction with metadata |

First run: unset the wallet key file and all three `RECORD_*` variables, then run
`dotnet test --filter 'Category=Integration'`. This performs reads and Blockfrost
parity without spending. Add the manifest's recorded address to the address list
to accept a fresh recording against Blockfrost in that same session.

Second run, only with separate approval for that execution: set the wallet key
file and both fee-budget variables, then run
`dotnet test --filter 'FullyQualifiedName~FundedTransferTests'`. Only an ADA-only
input is used; every output pays the wallet, and the fee must not exceed 1 ADA.
The ledger reserves the fee before submission, even if submission later fails;
it is never rolled back. No resubmission occurs. Waiting is bounded by ten
minutes, then output and metadata read-back by one minute. Only hash and fee are
written. A failed run requires fresh approval, not an automatic retry.

Dolos 1.6.3's four parameter gaps are named once in `DolosGaps`; recorded tests
assert those exact values and parity excludes only the affected parts. The
provider does not correct them. Datum hashes and reference-script bytes are
checked against Pallas's parsed recording, not Blockfrost's lossy reconstruction.

The primary public script address is the search recording and joins parity only
when explicitly added. The second capture is a `ReadUtxos` sample of eight
hash-datum outputs (including assets) at the public V1 always-succeeds contract.
That address has more than 50 outputs and anyone can spend from it, so it is used
only for offline golden replay, never for live reads, parity or funded tests.
`CHRYSALIS_UTXORPC_RECORD_HASH_REFS` optionally supplies its comma-separated
`hash#index` references to the recorder. Unset it in every acceptance run too.

Reference-script presence is skipped only when Blockfrost omits it: its
address-UTxO endpoint can omit `reference_script_hash` when its transaction-UTxO
endpoint reports it for the same immutable output. If Blockfrost reports a
script, UTxO RPC must report it too, with the same language. A separate manifested
transaction-endpoint hash golden checks the decoded native reference script.

To recapture the immutable Blockfrost hash, set the network, Blockfrost key-file
path and record directory, then run
`dotnet test --filter 'FullyQualifiedName~Recorder.RecordsReferenceScriptHash'`.
Only that test process reads the key and makes one request; it stores only the
endpoint, UTC date and unchanged hash. Update its manifest date and SHA-256,
then rebuild and run the recorded tests. Unset the record directory afterwards.

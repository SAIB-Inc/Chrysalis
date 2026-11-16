# Dolos preprod capture

Captured from Dolos v1.6.3 over plaintext HTTP/2 on preprod. Native and parsed
forms are the server's own bytes. Public chain data, own capture; no credentials
or wallet material. The manifest names every request, capture tip and date.

To record again, configure the endpoint, network and wallet address list as the
test README describes. Choose a third-party script address with fewer than 50
UTxOs, no native reference script, and the required output shapes; choose a
transaction with metadata inside the server's retained history. Get approval
before recording. Set `CHRYSALIS_UTXORPC_RECORD_DIR`,
`CHRYSALIS_UTXORPC_RECORD_ADDRESS` and `CHRYSALIS_UTXORPC_RECORD_TX` for
`dotnet test --filter 'FullyQualifiedName~Recorder.RecordsPublicAnswers'` only. Unset them afterwards.
Update the manifest's requests, tips, dates and SHA-256s. Rebuild, run the offline
recorded tests, and run parity with the recorded address added, in that session.

Limit: this server has the four parameter gaps listed in `DolosGaps`. When a new
capture comes from a fixed server, remove only the closed entries and this limit
line; do not edit its bytes or the node's unchanged reference to make tests pass.

`read-hash-utxos.pb` is a small reference sample from the public V1
always-succeeds contract. Its eight hash-datum outputs include native assets.
The full address is too large and mutable for parity; this capture is replayed
offline only. Supply its manifest references in
`CHRYSALIS_UTXORPC_RECORD_HASH_REFS` for the recorder run, then unset it too.

Blockfrost's address-UTxO endpoint omits `reference_script_hash` for the recorded
reference-script output, although its `/txs/{hash}/utxos` endpoint reports it.
`refscript-hash.tsv` captures only that immutable output's script hash, endpoint
and UTC time from the latter endpoint. Its provenance and SHA-256 are manifested.
The golden compares Chrysalis's public script hash of the native decoded script
with that independently captured value. Parity skips reference-script presence
only when Blockfrost omits it. When Blockfrost reports a script, UTxO RPC must
report it too, with the same language.

Recapture that scalar separately with
`dotnet test --filter 'FullyQualifiedName~Recorder.RecordsReferenceScriptHash'`,
with
`CHRYSALIS_TEST_BLOCKFROST_KEY_FILE`, the preprod network and record directory
set. The test process reads the key, calls the immutable endpoint once and
requires the same hash before replacing only the capture date. Update the
manifest date and SHA-256 afterwards; no complete response or header is stored.

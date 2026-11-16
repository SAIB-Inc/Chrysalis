# Cardano vectors

`cardano.Block` messages mapped from real Cardano blocks, one per protocol version.

| file | what it is |
|---|---|
| `blocks/` | the source blocks, as lowercase hex with their era wrapper |
| `v1alpha/`, `v1beta/` | the `cardano.Block` message for each block, protobuf-encoded |
| `vectors.tsv` | expected era, protocol version, slot, hash, transaction count and first transaction hash |

| block | header version | source |
|---|---|---|
| `babbage-v7` | 7 | Pallas `test_data/babbage1.block` |
| `babbage-v8` | 8 | Pallas `test_data/babbage7.block` |
| `conway-v9` | 9 | Pallas `test_data/conway1.block` |
| `conway-v10` | 10 | Pallas `test_data/conway4.block` |
| `conway-v11` | 11 | Mainnet slot 202315107, block `d03a37835e9f1ffb70e8a476334c69183ab5491c911faa7ea0d6ba285dd7c8b4` |
| `conway-v12` | 12 | Pallas `test_data/conway5.block`, Musashi testnet, last block before the fork |
| `dijkstra-v12` | 12 | Pallas `test_data/dijkstra3.block`, Musashi testnet |

Dijkstra isn't final, so treat `dijkstra-v12` as preliminary.

The vectors come from the mapper in `pallas-utxorpc`, built without ledger context, so
fields that need one are empty. They test decoding, not server behaviour, and a field the
schema doesn't have can't appear in them.

## Regenerating

```sh
cd generate && cargo run
```

# X10 tagged indefinite set fixture

`X10Tag258Indef.tx` is unmodified output from the official `cardano-cli 11.7.0.0` Darwin arm64 executable (binary SHA-256 `ae5f44c134b7062319634706c776738f06b3d7ff28ff705fa2e3835c74e43867`), run offline on the Mac. This is a Conway-era encoding (minimum protocol major 9); it did not query or use any node's current protocol version.

```bash
args=()
for i in {0..23}; do
    args+=(--tx-in "0000000000000000000000000000000000000000000000000000000000000001#$i")
done
cardano-cli conway transaction build-raw "${args[@]}" \
    --tx-out addr_test1vrapzvl8gvavjfy2d7qa4xvt8a6ytcsrq4ykr6azj27mylg59qvqy+2000000 \
    --fee 200000 --out-file X10Tag258Indef.tx
cardano-cli debug transaction view --tx-body-file X10Tag258Indef.tx --output-json
```

- Transaction envelope SHA-256: `e319ba89f6ce078248e3b8aea3f68d1f15ae3ea6f916f64814618489418f618f`.
- Decoded `cborHex` SHA-256: `f82669e14fe96f00001ef830999405b6300ee7a46df6be5c60dc738eade0a091`.
- Inputs: 24 distinct indexes under the same synthetic transaction ID. The input set spans CBOR offset 3, length 869, and starts `d9 01 02 9f`. Offline CLI replay reports all 24 inputs.
- The pinned ledger source `95b369053fd32422695a81fb7d5289d6dd35eee2`, `libs/cardano-ledger-binary/src/Cardano/Ledger/Binary/Encoding/Encoder.hs:459–491`, selects tag 258 from protocol 9 and indefinite lists over 23 items.

The other three list vectors are byte spans of `byron2.block` and `byron5.block` (Pallas v0.34.0; its pin and whole-file SHA-256 are in `pallas-manifest.tsv`) and the G1 block `block-139039741.cbor` (linked as `preprod-conway.block.cbor`; pin and SHA-256 in the network fixture manifest). They are read in place, not regenerated.

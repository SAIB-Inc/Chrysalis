# Koios offline test fixtures

Schema reference:

- Repository: `cardano-community/koios-artifacts`
- Tag: `v1.4.2`
- Commit: `d10752be2fcbcb6bd8c29978646ace2830049da2`
- Schema file: `specs/results/koiosapi-preprod.yaml`
- Schema SHA-256: `90a834d6b8dd7fdfcb19c31c20e09301d90923fbf9190cbd4ec24be0f7c060af`
- Schema licence: CC BY 4.0

`address_utxos.json` is written by hand from the `utxo_infos` field list in the pinned schema, with synthetic values and Chrysalis CLI vector addresses/scripts only — no Koios response text or example data is copied.

Row hashes:

- Rows 5 and 6 (Plutus V3): `blake2b-224(03 || 450101002499) = 186e32faa80a26810392fda6d559c7ed4721a65ce1c9d4ef3e1c87b4` (once-wrapped and legacy twice-wrapped `46450101002499`).
- Row 7 (native `timelock` `sig`): `blake2b-224(00 || 8200581cfa1133e7433ac9248a6f81da998b3f7445e203054961eba292bdb27d) = 751ebae7b78b2be4c0b6283f708becce05a802966b38b7f31d8758b2`.

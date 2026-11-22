# Ogmios offline test fixtures

Schema reference:

- Repository: `CardanoSolutions/ogmios`
- Tag: `v7.0.0`
- Commit: `b3a830a1bf2fbfd0ce1fd0fb238aee2d73d7cdbe`
- Schema files:
  - `docs/static/api/v7.0/ogmios.json` (SHA-256 `0604fde4077019307f470a6706410db72abb1487379e7d8a8284cf083d17620f`)
  - `docs/static/api/v7.0/cardano.json` (SHA-256 `830fc6fa6e324c9608f51126e0b8b72e453ff51e2492c1e7b6350ffc0de60508`)
- Encoder: `server/src/Ogmios/Data/Json/Conway.hs:558-660`
- Schema licence: MPL-2.0

`protocol-parameters.json` is written by hand from the schema's field list; no Ogmios file text or example is copied. Values come from the G1 Preprod snapshot (`TestData/cli/inputs/g1/current-protocol-parameters.json`), with `plutusCostModels` filled from G1 by the tests so cost models keep one copy.

# UTxO RPC messages

The unchanged files under `utxorpc/` come from `utxorpc/spec` v0.19.2.
Their source commits and SHA-256 hashes are in the test project's golden manifest.
To update, copy `proto/utxorpc/v1beta/{cardano,query,submit}/*.proto` and `LICENSE`
from a reviewed spec tag, then update that manifest's tag, commit and hashes.
Run the provider tests, including the public-surface and manifest checks.
C# is generated internally under `obj/` at build time; never check it in.

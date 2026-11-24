# UTxO RPC messages

`chrysalis/utxorpc/lean.proto` reads only the bytes and references the provider maps; every other UTxO RPC message comes from the `Utxorpc.Spec` package.
Its field numbers are those of the spec's `TxoRef`, `AnyUtxoData`, `SearchUtxosResponse`, `ReadUtxosResponse`, `AnyChainTx` and `ReadTxResponse`; the tests send the package's messages and read them through it.
After changing the package version, run the provider tests and `benchmarks/UtxoRpcBench`.
C# is generated internally under `obj/` at build time; never check it in.

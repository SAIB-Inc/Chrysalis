<div align="center">
  <h1>Chrysalis</h1>
  <p><strong>A native .NET toolkit for Cardano blockchain development</strong></p>

  <a href="https://www.nuget.org/packages/Chrysalis">
    <img src="https://img.shields.io/nuget/vpre/Chrysalis.svg?style=flat-square" alt="NuGet">
  </a>
  <a href="https://github.com/SAIB-Inc/Chrysalis/blob/main/LICENSE.md">
    <img src="https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square" alt="License">
  </a>
  <a href="https://github.com/SAIB-Inc/Chrysalis/stargazers">
    <img src="https://img.shields.io/github/stars/SAIB-Inc/Chrysalis.svg?style=flat-square" alt="Stars">
  </a>
  <a href="https://dotnet.microsoft.com/download">
    <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square" alt=".NET">
  </a>
  <a href="https://cardano.org/">
    <img src="https://img.shields.io/badge/Cardano-Compatible-0033AD?style=flat-square" alt="Cardano">
  </a>
</div>

<br>

CBOR serialization, transaction building, wallet management, Ouroboros mini-protocols, and a pure managed Plutus VM — everything you need to build on Cardano in .NET.

## Installation

```bash
dotnet add package Chrysalis --prerelease
```

Or install individual packages:

```bash
dotnet add package Chrysalis.Codec   --prerelease   # CBOR serialization + source generation
dotnet add package Chrysalis.Network --prerelease   # Ouroboros mini-protocols (N2C/N2N)
dotnet add package Chrysalis.Tx      --prerelease   # Transaction building + fee calculation
dotnet add package Chrysalis.Wallet  --prerelease   # Key management + address handling
dotnet add package Chrysalis.Plutus  --prerelease   # Pure managed UPLC/CEK machine
```

## Architecture

| Package | Description |
|---|---|
| **Chrysalis.Codec** | Attribute-driven CBOR serialization with source-generated encoders/decoders |
| **Chrysalis.Codec.CodeGen** | Source generator for compile-time CBOR dispatch + CIP-0057 blueprint codegen |
| **Chrysalis.Network** | Ouroboros N2C/N2N mini-protocols with pipelined ChainSync + BlockFetch |
| **Chrysalis.Tx** | `TransactionBuilder`, `MintBuilder`, `OutputBuilder`, fee/collateral calculation |
| **Chrysalis.Plutus** | Pure C# UPLC interpreter — 999/999 conformance tests, no native dependencies |
| **Chrysalis.Wallet** | BIP-39 mnemonic, BIP-32 HD key derivation, Bech32 addresses |
| **Chrysalis.Crypto** | Ed25519 signatures, Blake2b hashing |

## Quick Start

### Generate Types from Aiken Blueprints

Drop an Aiken-compiled `plutus.json` into your project and get fully typed, serializable C# types at compile time — no manual type definitions needed.

```xml
<!-- .csproj -->
<ItemGroup>
  <ProjectReference Include="Chrysalis.Codec.CodeGen"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <AdditionalFiles Include="plutus.json" />
</ItemGroup>
```

The source generator reads CIP-0057 blueprint schemas and emits records with full CBOR serialization:

```csharp
// Auto-generated from plutus.json — these types appear in IntelliSense immediately
using WizardProtocol.P2p.Blueprint;

// Construct values with Create()
var datum = WizardDatum.Create(
    kind: AutoLimit.Create(),
    assetPair: TupleTypesAssetTypesAsset.Create(asset1, asset2),
    swapPrice: OneWay.Create(rational),
    minimumPrice: None<ISwap>.Create(),
    owner: Signature.Create(PlutusBoundedBytes.Create(ownerKeyHash))
);

// Serialize — produces byte-identical CBOR to Aiken's cbor.serialise
byte[] cbor = CborSerializer.Serialize(datum);

// Deserialize
WizardDatum decoded = CborSerializer.Deserialize<WizardDatum>(cbor);
```

### Define Plutus Data Types Manually

For types not in a blueprint, define them with attributes:

```csharp
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

// Single attribute for Plutus datum/redeemer types
[PlutusData(0)]
public partial record MyDatum(
    [CborOrder(0)] byte[] Owner,
    [CborOrder(1)] ulong Amount
) : CborRecord;

// Union types for redeemers with multiple constructors
[CborSerializable]
[CborUnion]
public abstract partial record MyRedeemer : CborRecord;

[PlutusData(0)]
public partial record Spend(
    [CborOrder(0)] long OutputIndex
) : MyRedeemer;

[PlutusData(1)]
public partial record Cancel() : MyRedeemer;
```

### Wallet

```csharp
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;

// Generate a 24-word mnemonic (English word list is the default)
Mnemonic mnemonic = Mnemonic.Generate(24);

// Derive keys with BIP-44 helpers
PrivateKey accountKey = mnemonic.GetRootKey().DeriveCardanoAccountKey();
PrivateKey paymentKey = accountKey.DerivePaymentKey();
PublicKey stakingPub = accountKey.DeriveStakeKey().GetPublicKey();

// Generate address
Address address = Address.FromPublicKeys(
    NetworkType.Testnet,
    AddressType.BasePayment,
    paymentKey.GetPublicKey(),
    stakingPub
);

Console.WriteLine(address.ToBech32());
// addr_test1qz...
```

### Build Transactions

```csharp
using Chrysalis.Tx.Builders;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;

TransactionBuilder builder = TransactionBuilder.Create(pparams);

// Add inputs (hex strings or txid#index format)
builder.AddInput("a1b2c3d4...#0");

// Fluent output builder with auto min-ADA
builder.AddOutput("addr_test1qz...", Lovelace.Create(5_000_000))
    .WithInlineDatum(myDatum)
    .WithMinAda(pparams.AdaPerUTxOByte)
    .Add();

// Simple ADA-only output
builder.AddOutput("addr_test1qz...", Lovelace.Create(2_000_000))
    .Add();

// Multi-asset output — no nested dictionary ceremony
IValue value = Value.FromLovelace(2_000_000)
    .WithToken(policyHex, assetNameHex, 100)
    .WithToken(policyHex, assetNameHex2, 50);

builder.AddOutput("addr_test1qz...", value).Add();
```

### Mint Tokens

```csharp
using Chrysalis.Tx.Builders;

// Fluent mint builder
MultiAssetMint mint = MintBuilder.Create()
    .AddToken(policyHex, assetNameHex, 1_000)   // mint
    .AddToken(policyHex, assetNameHex2, -1)      // burn
    .Build();

builder.SetMint(mint);

// Or directly on the builder
builder.AddMint(policyHex, assetNameHex, 1_000);
```

### Value Arithmetic

```csharp
// Merge two values (adds lovelace + combines tokens)
IValue total = value1.Merge(value2);

// Subtract
IValue remaining = total.Subtract(spent);

// Query token amounts
ulong? qty = value.QuantityOf(policyHex, assetNameHex);

// Flat dictionary of all assets
Dictionary<string, ulong> assets = value.ToAssetDictionary();
// Keys: "lovelace", "policyHex+assetNameHex"
```

### Sign and Submit

```csharp
using Chrysalis.Tx.Extensions;

// Single key
ITransaction signed = tx.Sign(paymentKey);

// Multiple keys (hashes tx body once)
ITransaction signed = tx.Sign(paymentKey, stakeKey, scriptKey);

// Submit via Blockfrost
string txHash = await provider.SubmitTransactionAsync(signed);
```

### Read Inline Datums

```csharp
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;

// One-liner datum access — no triple-casting
MyDatum? datum = output.InlineDatum<MyDatum>();
ReadOnlyMemory<byte>? rawCbor = output.InlineDatumRaw();
```

### Node Communication

```csharp
using Chrysalis.Network.Multiplexer;

// N2C: connect to local node via Unix socket
NodeClient node = await NodeClient.ConnectAsync("/ipc/node.socket");
await node.StartAsync(networkMagic);

// Query chain tip
var tip = await node.LocalStateQuery.GetTipAsync();

// Query UTxOs
var utxos = await node.LocalStateQuery.GetUtxosByAddressAsync([addressBytes]);

// Submit transactions
await node.LocalTxSubmit.SubmitTxAsync(signedTxBytes);

// N2N: connect to remote node via TCP
PeerClient peer = await PeerClient.ConnectAsync("relay.cardano.org", 3001);
await peer.StartAsync(networkMagic);

// Pipelined ChainSync + BlockFetch
await peer.ChainSync.FindIntersectionAsync([Point.Origin], ct);
```

### Plutus VM

Pure managed UPLC interpreter — no Haskell, no Rust, no FFI.

```csharp
using Chrysalis.Tx.Extensions;

// Evaluate all scripts in a transaction
IReadOnlyList<EvaluationResult> results = ScriptContextBuilder.EvaluateTx(
    body, witnessSet, utxos, SlotNetworkConfig.Preview);

foreach (var r in results)
    Console.WriteLine($"Redeemer [{r.RedeemerTag}:{r.Index}] mem={r.ExUnits.Mem} steps={r.ExUnits.Steps}");
```

The VM passes **999/999 UPLC conformance tests** covering Plutus V1-V3, including all 94 builtins and BLS12-381 cryptographic primitives. The transaction builder uses it automatically — no external evaluator needed.

### Script Addresses

```csharp
using Chrysalis.Wallet.Models.Addresses;

// Compute script hash from IScript
string scriptHash = myScript.HashHex();

// Create script address
Address scriptAddr = Address.FromScriptHash(NetworkType.Testnet, scriptHash);

// With staking credential
Address scriptAddr = Address.FromScriptHashWithStake(
    NetworkType.Testnet, scriptHash, stakeKeyHash);
```

## Performance

Benchmarks against [Pallas](https://github.com/txpipe/pallas) (Rust) and [Gouroboros](https://github.com/blinklabs-io/gouroboros) (Go) on Conway-era blocks, local Cardano Preview node.

**N2N (TCP) — Pipelined ChainSync from origin:**

| | Headers Only | Full Blocks + Deser |
|---|---|---|
| **Chrysalis (.NET)** | **~35,000 blk/s** | **~9,500 blk/s** |
| **Gouroboros (Go)** | ~15,000 blk/s | N/A |
| **Pallas (Rust)** | N/A | ~720 blk/s |

**N2C (Unix Socket) — 10,000 blocks, sequential:**

| | With Deserialization | Network Only |
|---|---|---|
| **Pallas (Rust)** | 3,097 blk/s | 3,280 blk/s |
| **Chrysalis (.NET)** | 2,747 blk/s | 2,977 blk/s |
| **Gouroboros (Go)** | 2,735 blk/s | N/A |

N2C is bottlenecked by the node — all three converge around 2,700-3,300 blk/s. On N2N where pipelining matters, Chrysalis is **2.3x faster than Go** and **13x faster than Rust** on full block download.

<details>
<summary>How it's fast</summary>

- **Batch burst pipelining** — send N header requests, drain N responses, BlockFetch the batch
- **Zero-copy deserialization** — `ReadOnlyMemory<byte>` throughout, no intermediate allocations
- **Source-generated CBOR dispatch** — compile-time probe-based union resolution via PeekState/PeekTag
- **System.IO.Pipelines** — backpressure-aware async I/O with minimal buffer copies

AMD Ryzen 9 9900X3D, .NET 10. Full results in [`benchmarks/BENCHMARKS.md`](benchmarks/BENCHMARKS.md).
</details>

## Era Support

| Era | Serialization | Block Processing | Tx Building | Script Eval |
|---|:---:|:---:|:---:|:---:|
| **Byron** | ✅ | ✅ | — | — |
| **Shelley** | ✅ | ✅ | ✅ | — |
| **Allegra** | ✅ | ✅ | ✅ | — |
| **Mary** | ✅ | ✅ | ✅ | — |
| **Alonzo** | ✅ | ✅ | ✅ | ✅ |
| **Babbage** | ✅ | ✅ | ✅ | ✅ |
| **Conway** | ✅ | ✅ | ✅ | ✅ |

## Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Commit your changes: `git commit -m 'feat: add my feature'`
4. Push and open a Pull Request

## License

MIT — see [LICENSE.md](LICENSE.md).

---

<div align="center">
  <p>Built by <a href="https://saib.dev">SAIB Inc</a> for the Cardano community</p>
</div>

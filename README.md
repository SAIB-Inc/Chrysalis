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

## Packages

| Package | Description |
|---|---|
| **Chrysalis.Codec** | Attribute-driven CBOR serialization with source-generated encoders/decoders |
| **Chrysalis.Codec.CodeGen** | Source generator for compile-time CBOR dispatch + CIP-0057 blueprint codegen |
| **Chrysalis.Network** | Ouroboros N2C/N2N mini-protocols with pipelined ChainSync + BlockFetch |
| **Chrysalis.Tx** | `TransactionBuilder`, `MintBuilder`, `OutputBuilder`, fee/collateral calculation |
| **Chrysalis.Plutus** | Pure C# UPLC interpreter — 999/999 conformance tests, no native dependencies |
| **Chrysalis.Wallet** | BIP-39 mnemonic, BIP-32 HD key derivation, Bech32 addresses |
| **Chrysalis.Crypto** | Ed25519 signatures, Blake2b hashing |

## Blueprint Codegen

Drop an Aiken-compiled `plutus.json` into your project and get fully typed, serializable C# types at compile time.

```xml
<!-- .csproj -->
<ItemGroup>
  <PackageReference Include="Chrysalis.Codec" Version="*-*" />
  <PackageReference Include="Chrysalis.Codec.CodeGen" Version="*-*"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <AdditionalFiles Include="plutus.json" />
</ItemGroup>
```

The source generator reads [CIP-0057](https://cips.cardano.org/cip/CIP-0057) blueprint schemas and emits records with full CBOR serialization — types appear in IntelliSense immediately:

```csharp
using MyProject.Blueprint;

// Construct from values — serializes byte-identical to Aiken's cbor.serialise
var credential = VerificationKeyCredential.Create(PlutusBoundedBytes.Create(keyHash));
var address = Address.Create(credential, None<ICredential>.Create());
var datum = SimpleDatum.Create(address, PlutusInt.Create(1_000_000),
    PlutusBoundedBytes.Create(tag), PlutusTrue.Create());

byte[] cbor = CborSerializer.Serialize(datum);

// Deserialize back into the generated type
SimpleDatum decoded = CborSerializer.Deserialize<SimpleDatum>(cbor);
```

<details>
<summary>Define types manually (without a blueprint)</summary>

```csharp
[PlutusData(0)]
public partial record MyDatum(
    [CborOrder(0)] byte[] Owner,
    [CborOrder(1)] ulong Amount
) : CborRecord;

[CborSerializable]
[CborUnion]
public abstract partial record MyRedeemer : CborRecord;

[PlutusData(0)]
public partial record Spend([CborOrder(0)] long OutputIndex) : MyRedeemer;

[PlutusData(1)]
public partial record Cancel() : MyRedeemer;
```

</details>

## Transaction Building

```csharp
TransactionBuilder builder = TransactionBuilder.Create(pparams);

builder.AddInput("a1b2c3d4...#0");

builder.AddOutput("addr_test1qz...", Lovelace.Create(5_000_000))
    .WithInlineDatum(myDatum)
    .WithMinAda(pparams.AdaPerUTxOByte)
    .Add();

// Multi-asset outputs
IValue value = Value.FromLovelace(2_000_000)
    .WithToken(policyHex, assetNameHex, 100);
builder.AddOutput("addr_test1qz...", value).Add();

// Minting
builder.AddMint(policyHex, assetNameHex, 1_000);

// Sign and submit
ITransaction signed = tx.Sign(paymentKey, stakeKey);
string txHash = await provider.SubmitTransactionAsync(signed);
```

<details>
<summary>Value arithmetic</summary>

```csharp
IValue total = value1.Merge(value2);
IValue remaining = total.Subtract(spent);
ulong? qty = value.QuantityOf(policyHex, assetNameHex);
Dictionary<string, ulong> assets = value.ToAssetDictionary();
```

</details>

<details>
<summary>Read inline datums</summary>

```csharp
MyDatum? datum = output.InlineDatum<MyDatum>();
ReadOnlyMemory<byte>? rawCbor = output.InlineDatumRaw();
```

</details>

## Wallet

```csharp
Mnemonic mnemonic = Mnemonic.Generate(24);

PrivateKey accountKey = mnemonic.GetRootKey().DeriveCardanoAccountKey();
PrivateKey paymentKey = accountKey.DerivePaymentKey();
PublicKey stakingPub = accountKey.DeriveStakeKey().GetPublicKey();

Address address = Address.FromPublicKeys(
    NetworkType.Testnet, AddressType.BasePayment,
    paymentKey.GetPublicKey(), stakingPub);
```

## Node Communication

```csharp
// N2C: local node via Unix socket
NodeClient node = await NodeClient.ConnectAsync("/ipc/node.socket");
await node.StartAsync(networkMagic);

var tip = await node.LocalStateQuery.GetTipAsync();
var utxos = await node.LocalStateQuery.GetUtxosByAddressAsync([addressBytes]);
await node.LocalTxSubmit.SubmitTxAsync(signedTxBytes);

// N2N: remote node via TCP with pipelined sync
PeerClient peer = await PeerClient.ConnectAsync("relay.cardano.org", 3001);
await peer.StartAsync(networkMagic);
await peer.ChainSync.FindIntersectionAsync([Point.Origin], ct);
```

## Plutus VM

Pure managed UPLC interpreter — no Haskell, no Rust, no FFI. **999/999 conformance tests** covering Plutus V1-V3, all 94 builtins, and BLS12-381 cryptographic primitives.

```csharp
IReadOnlyList<EvaluationResult> results = ScriptContextBuilder.EvaluateTx(
    body, witnessSet, utxos, SlotNetworkConfig.Preview);

foreach (var r in results)
    Console.WriteLine($"[{r.RedeemerTag}:{r.Index}] mem={r.ExUnits.Mem} steps={r.ExUnits.Steps}");
```

The transaction builder uses the VM automatically — no external evaluator needed.

## Performance

Benchmarks against [Pallas](https://github.com/txpipe/pallas) (Rust) and [Gouroboros](https://github.com/blinklabs-io/gouroboros) (Go) on Conway-era blocks.

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

<div align="center">
  <img src="assets/banner.png" alt="Chrysalis Banner" width="100%">
  
  <a href="https://www.nuget.org/packages/Chrysalis">
    <img src="https://img.shields.io/nuget/v/Chrysalis.svg?style=flat-square" alt="NuGet">
  </a>
  <a href="https://github.com/SAIB-Inc/Chrysalis/blob/main/LICENSE.md">
    <img src="https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square" alt="License">
  </a>
  <a href="https://github.com/SAIB-Inc/Chrysalis/fork">
    <img src="https://img.shields.io/github/forks/SAIB-Inc/Chrysalis.svg?style=flat-square" alt="Forks">
  </a>
  <a href="https://github.com/SAIB-Inc/Chrysalis/stargazers">
    <img src="https://img.shields.io/github/stars/SAIB-Inc/Chrysalis.svg?style=flat-square" alt="Stars">
  </a>
  <a href="https://github.com/SAIB-Inc/Chrysalis/graphs/contributors">
    <img src="https://img.shields.io/github/contributors/SAIB-Inc/Chrysalis.svg?style=flat-square" alt="Contributors">
  </a>
  <br>
  <a href="https://dotnet.microsoft.com/download">
    <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square" alt=".NET">
  </a>
  <a href="https://cardano.org/">
    <img src="https://img.shields.io/badge/Cardano-Compatible-0033AD?style=flat-square" alt="Cardano">
  </a>
</div>

## 📖 Overview

Chrysalis is a native .NET toolkit for Cardano blockchain development, providing everything needed to build applications on Cardano. From CBOR serialization to transaction building and smart contract interaction, Chrysalis offers a complete solution for .NET developers.

**Key Components:**

- 📦 **Serialization** - Efficient CBOR encoding/decoding for Cardano data structures
- 🔄 **Node Communication** - Direct interaction with Cardano nodes through Ouroboros mini-protocols
- 🔑 **Wallet Management** - Address generation and key handling
- 💳 **Transaction Building** - Simple and advanced transaction construction
- 📜 **Smart Contract Integration** - Plutus script evaluation and validation via Rust FFI

## ✨ Features

- 🔐 **Type-Safe Data Models** - Strong typing for all Cardano blockchain structures
- ⚡ **High Performance** - Optimized for speed and efficiency
- 🧩 **Modular Architecture** - Use only what you need
- 🚀 **Modern C# API** - Takes advantage of the latest .NET features
- 🔗 **Complete Cardano Support** - Works with all major Cardano eras and protocols

## 📥 Installation

```bash
# Install the main package
dotnet add package Chrysalis
```

Or install individual components:

```bash
dotnet add package Chrysalis.Cbor
dotnet add package Chrysalis.Network
dotnet add package Chrysalis.Tx
dotnet add package Chrysalis.Plutus
dotnet add package Chrysalis.Wallet
```

## 🧩 Architecture

Chrysalis consists of several specialized libraries:

| Module                     | Description                                        |
| -------------------------- | -------------------------------------------------- |
| **Chrysalis.Cbor**         | CBOR serialization for Cardano data structures     |
| **Chrysalis.Cbor.CodeGen** | Source generation for optimized serialization code |
| **Chrysalis.Network**      | Implementation of Ouroboros mini-protocols         |
| **Chrysalis.Tx**           | Transaction building and submission                |
| **Chrysalis.Plutus**       | Smart contract evaluation and validation           |
| **Chrysalis.Wallet**       | Key management and address handling                |

## 💻 Usage Examples

### 📦 CBOR Serialization

Define and use CBOR-serializable types with attribute-based serialization:

```csharp
// Define CBOR-serializable types
[CborSerializable]
[CborConstr(0)]
public partial record AssetDetails(
    [CborOrder(0)] byte[] PolicyId,
    [CborOrder(1)] AssetClass Asset,
    [CborOrder(2)] ulong Amount
): CborBase;

[CborSerializable]
[CborList]
public partial record AssetClass(
    [CborOrder(0)] byte[] PolicyId,
    [CborOrder(1)] byte[] AssetName
) : CborBase;

// Deserialize from CBOR hex
var data = "d8799f581cc05cb5c5f43aac9d9e057286e094f60d09ae61e8962ad5c42196180c9f4040ff1a00989680ff";
AssetDetails details = CborSerializer.Deserialize<AssetDetails>(data);

// Serialize back to CBOR
byte[] serialized = CborSerializer.Serialize(details);
```

#### Extension Method Pattern

Chrysalis uses extension methods extensively to provide clean access to nested data structures:

```csharp
// Without extensions, deep property access is verbose and differs by era
var hash = transaction.TransactionBody.Inputs.GetValue()[0].TransactionId;

// With extension methods, access is simplified and era-agnostic
var hash = transaction.TransactionBody.Inputs().First().TransactionId();

// Extensions support common operations
Transaction signedTx = transaction.Sign(privateKey);
```

### 🔑 Wallet Management

Generate and manage addresses and keys:

```csharp
// Create wallet from mnemonic
var mnemonic = Mnemonic.Generate(English.Words, 24);

var accountKey = mnemonic
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

var privateKey = accountKey
            .Derive(RoleType.ExternalChain)
            .Derive(0);

var paymentKey = privateKey.GetPublicKey();

var stakingKey = accountKey
            .Derive(RoleType.Staking)
            .Derive(0)
            .GetPublicKey();

// Generate address
var address = Address.FromPublicKeys(
    NetworkType.Testnet,
    AddressType.BasePayment,
    paymentKey,
    stakingKey
);

string bech32Address = address.ToBech32();

Console.WriteLine($"Bech32 Address: {bech32Address}");
```

### 🔄 Node Communication

Connect directly to a Cardano node:

```csharp
try {
    // Connect to a local node
    NodeClient client = await NodeClient.ConnectAsync("/ipc/node.socket");
    await client.StartAsync(2);

    // Query UTXOs by address
    var addressBytes = Convert.FromHexString("00a7e1d2e57b1f9aa851b08c8934a315ffd97397fa997bb3851c626d3bb8d804d91fa134757d1a41b0b12762f8922fe4b4c6faa5ffec1bc9cf");
    var utxos = await client.LocalStateQuery.GetUtxosByAddressAsync([addressBytes]);

    // Synchronize with the chain
    var tip = await client.LocalStateQuery.GetTipAsync();
    Console.WriteLine($"Chain tip: {tip}");

    // Available mini-protocols - accessed as properties
    var localTxSubmit = client.LocalTxSubmit;
    var localStateQuery = client.LocalStateQuery;
    var localTxMonitor = client.LocalTxMonitor;
}
catch (InvalidOperationException ex) {
    Console.WriteLine($"Connection failed: {ex.Message}");
}
catch (Exception ex) {
    Console.WriteLine($"Protocol error: {ex.Message}");
}
```

### 💳 Transaction Building

Build and sign transactions with the fluent API or template builder:

```csharp
// Simple transaction using template builder
var senderAddress = address.ToBech32();
var receiverAddress = "addr_test1qpcxqfg6xrzqus5qshxmgaa2pj5yv2h9mzm22hj7jct2ad59q2pfxagx7574360xl47vhw79wxtdtze2z83k5a4xpptsm6dhy7";
var provider = new Blockfrost("apiKeyHere");

var transfer = TransactionTemplateBuilder<ulong>.Create(provider)
    .AddStaticParty("sender", senderAddress, true)
    .AddStaticParty("receiver", receiverAddress)
    .AddInput((options, amount) =>
    {
        options.From = "sender";
    })
    .AddOutput((options, amount) =>
    {
        options.To = "receiver";
        options.Amount = new Lovelace(amount);
    })
    .Build();

// Execute the template with a specific amount
Transaction tx = await transfer(5_000_000UL);
Transaction signedTx = tx.Sign(privateKey);
```

### 📜 Smart Contract Interaction

Interact with and validate Plutus scripts:

```csharp
var provider = new Blockfrost("project_id");
var ownerAddress = "your address";
var alwaysTrueValidatorAddress = "your validator address";

var spendRedeemerBuilder = (_, _, _) =>
{
    // Custom Logic and return type as long as it inherits from CborBase
    // ex: returns an empty list
    return new PlutusConstr([]);
};

var lockTxHash = "your locked tx hash";
var scriptRefTxHash = "your script ref tx hash";

var unlockLovelace = TransactionTemplateBuilder<UnlockParameters>.Create(provider)
    .AddStaticParty("owner", ownerAddress, true)
    .AddStaticParty("alwaysTrueValidator", alwaysTrueValidatorAddress)
    .AddReferenceInput((options, unlockParams) =>
    {
        options.From = "alwaysTrueValidator";
        options.UtxoRef = unlockParams.ScriptRefUtxoOutref;
    })
    .AddInput((options, unlockParams) =>
    {
        options.From = "alwaysTrueValidator";
        options.UtxoRef = unlockParams.LockedUtxoOutRef;
        options.SetRedeemerBuilder(spendRedeemerBuilder);
    })
    .Build();


var unlockParams = new(
    new TransactionInput(Convert.FromHexString(lockTxHash), 0),
    new TransactionInput(Convert.FromHexString(scriptRefTxHash), 0)
);

var unlockUnsignedTx = await unlockLovelace(unlockParams);
var unlockSignedTx = unlockUnsignedTx.Sign(privateKey);
var unlockTxHash = await provider.SubmitTransactionAsync(unlockSignedTx);

Console.WriteLine($"Unlock Tx Hash: {unlockTxHash}");
```

#### CIP Implementation Support

Chrysalis supports various Cardano Improvement Proposals (CIPs), including:

```csharp
// CIP-68 NFT standard implementation
var nftMetadata = new Cip68<PlutusData>(
    Metadata: metadata,
    Version: 1,
    Extra: null
);
```

## ⚡ Performance

.NET can compete with Rust and Go. Chrysalis proves it.

Benchmarks compare Chrysalis against [Pallas](https://github.com/txpipe/pallas) (Rust) and [Gouroboros](https://github.com/blinklabs-io/gouroboros) (Go) — the two most established Cardano client libraries in systems languages — on Conway-era blocks against a local Cardano Preview testnet node.

**N2N (TCP) — Pipelined Header Sync from origin (apples-to-apples):**

| | blk/s |
|---|---|
| **Chrysalis (.NET)** | **~40,000** |
| **Gouroboros (Go)** | ~14,500 |
| **Pallas (Rust)** | ~722 |

> Chrysalis is **2.8x faster than Go** and **55x faster than Rust** on raw pipelined header throughput — same pipeline depth (100), same workload, same node.

**N2N (TCP) — Full Block Download + Deserialization from origin:**

| | blk/s | Notes |
|---|---|---|
| **Chrysalis (.NET)** | **~9,500** | Pipelined ChainSync + BlockFetch + full deserialization |
| **Gouroboros (Go)** | — | Cannot run concurrent ChainSync + BlockFetch (node queue overflow) |
| **Pallas (Rust)** | ~722 | Sequential (no pipelining) |

**N2C (Unix Socket) — 10,000 blocks, sequential:**

| | With Deserialization | Network Only |
|---|---|---|
| **Pallas (Rust)** | 3,097 blk/s | 3,280 blk/s |
| **Chrysalis (.NET)** | 2,747 blk/s | 2,977 blk/s |
| **Gouroboros (Go)** | 2,735 blk/s | — |

> N2C is bottlenecked by the node, not the client — all three converge around 2,700–3,300 blk/s. Chrysalis reaches 89% of Rust here.

**How:**

- **Batch burst pipelining** — send N requests, drain N responses, BlockFetch the batch. Eliminates per-block round-trip latency
- **Zero-copy deserialization** — `ReadOnlyMemory<byte>` throughout the pipeline, no intermediate allocations
- **Source-generated CBOR dispatch** — compile-time probe-based union resolution via PeekState/PeekTag instead of try-catch
- **System.IO.Pipelines** — backpressure-aware async I/O with minimal buffer copies

Benchmarks run on AMD Ryzen 9 9900X3D, .NET 10. Full results and methodology in [`benchmarks/BENCHMARKS.md`](benchmarks/BENCHMARKS.md).

## 🔄 Cardano Era Support

Chrysalis provides comprehensive support for Cardano's evolution:

<table>
<thead>
  <tr>
    <th>Era</th>
    <th>Phase</th>
    <th>Status</th>
    <th colspan="3">Feature Support</th>
  </tr>
  <tr>
    <th></th>
    <th></th>
    <th></th>
    <th align="center">Serialization</th>
    <th align="center">Block Processing</th>
    <th align="center">Transaction Building</th>
  </tr>
</thead>
<tbody>
  <tr>
    <td><strong>Byron</strong></td>
    <td>Foundation</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">❌</td>
  </tr>
  <tr>
    <td><strong>Shelley</strong></td>
    <td>Decentralization</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
  </tr>
  <tr>
    <td><strong>Allegra</strong></td>
    <td>Token Locking</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
  </tr>
  <tr>
    <td><strong>Mary</strong></td>
    <td>Multi-Asset</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
  </tr>
  <tr>
    <td><strong>Alonzo</strong></td>
    <td>Smart Contracts</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
  </tr>
  <tr>
    <td><strong>Babbage/Vasil</strong></td>
    <td>Scaling</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
  </tr>
  <tr>
    <td><strong>Conway</strong></td>
    <td>Governance</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
    <td align="center">✅</td>
  </tr>
</tbody>
</table>

**Legend**:

- ✅ Fully Supported
- 🚧 Planned for Future Release
- ❌ Not Supported Yet

## 📚 Documentation

For detailed documentation on each component:

- [Chrysalis.Cbor Documentation](./docs/CBOR.md)
- [Chrysalis.Tx Documentation](./docs/TX.md)
- [API Documentation](https://docs.chrysalis.dev) - Coming soon
- [Getting Started Guide](https://docs.chrysalis.dev/guides/getting-started) - Coming soon

> Note: The documentation is currently in development. In the meantime, this README and the code examples provide a good starting point.

### Native Library Dependencies

The Plutus VM integration currently requires Rust-based native libraries that are automatically included with the NuGet package. We are actively working towards a pure .NET implementation of the Plutus Virtual Machine for improved cross-platform compatibility and performance.

Current native dependencies:

- Linux: `libpallas_dotnet_rs.so` and `libplutus_vm_dotnet_rs.so`
- macOS: `libplutus_vm_dotnet_rs.dylib`

## 🤝 Contributing

We welcome contributions! To get started:

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`
3. Commit your changes: `git commit -m 'feat: add amazing feature'`
4. Push to the branch: `git push origin feature/amazing-feature`
5. Open a Pull Request

Please make sure to update tests as appropriate.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

---

<div align="center">
  <p>Made with ❤️ by <a href="https://saib.dev">SAIB Inc</a> for the Cardano community</p>
</div>

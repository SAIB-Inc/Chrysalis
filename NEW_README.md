# 🦋 Chrysalis: The .NET Ecosystem for Cardano Blockchain

<div align="center">
    <a href="https://github.com/SAIB-Inc/Chrysalis/fork">
        <img src="https://img.shields.io/github/forks/SAIB-Inc/Chrysalis.svg?style=flat-square" alt="Forks">
    </a>
    <a href="https://github.com/SAIB-Inc/Chrysalis/stargazers">
        <img src="https://img.shields.io/github/stars/SAIB-Inc/Chrysalis.svg?style=flat-square" alt="Stars">
    </a>
    <a href="https://github.com/SAIB-Inc/Chrysalis/blob/main/LICENSE.md">
        <img src="https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square" alt="License">
    </a>
    <a href="https://github.com/SAIB-Inc/Chrysalis/graphs/contributors">
        <img src="https://img.shields.io/github/contributors/SAIB-Inc/Chrysalis.svg?style=flat-square" alt="Contributors">
    </a>
    <br>
    <a href="https://www.nuget.org/packages/Chrysalis">
        <img src="https://img.shields.io/nuget/v/Chrysalis.svg?style=flat-square" alt="NuGet">
    </a>
    <a href="https://learn.microsoft.com/dotnet/">
        <img src="https://img.shields.io/badge/C%23-purple.svg?style=flat-square" alt="C#">
    </a>
    <a href="https://dotnet.microsoft.com/download">
        <img src="https://img.shields.io/badge/.NET-9.0-blue.svg?style=flat-square" alt=".NET">
    </a>
</div>

## ✨ Overview

**Chrysalis** is a comprehensive, native .NET ecosystem for Cardano blockchain development. It provides .NET developers with a complete toolkit for interacting with the Cardano blockchain through:

- **CBOR Serialization** - Foundation for all Cardano data
- **Network Communications** - Connect directly to Cardano nodes
- **Wallet Management** - Create and manage addresses and keys
- **Transaction Building** - Construct and sign complex transactions
- **Smart Contract Integration** - Interact with and validate Plutus scripts

Whether you're building a wallet, explorer, DApp backend, or any Cardano-based application in .NET, Chrysalis provides the building blocks you need.

## 🚀 Key Features

### Comprehensive Data Models
- Complete implementation of Cardano's complex data structures (blocks, transactions, scripts)
- Support for Cardano's recent eras:
  - **Shelley era** extensions (Allegra, Mary)
  - **Goguen era** (Alonzo)
  - **Basho era** (Babbage/Vasil)
  - **Voltaire era** (Conway)
- Type-safe access to blockchain data

> **Note:** Byron era support is planned for future releases

### High-Performance Serialization
- Native CBOR encoding/decoding (no JavaScript bridges)
- Attribute-driven serialization model
- Source generators for serialization boilerplate
- Optimized for performance with memory pooling

### Node Communication
- Direct connection to Cardano nodes
- Implementation of Ouroboros mini-protocols
- Chain synchronization and state queries
- Transaction submission and monitoring

### Wallet & Address Management
- Implementation of key Cardano Improvement Proposals (CIPs)
- Address specification compliant with [CIP-0019](https://cips.cardano.org/cips/cip19/)
- Multi-signature support as defined in [CIP-0011](https://cips.cardano.org/cips/cip11/)
- HD wallet scheme following [CIP-1852](https://cips.cardano.org/cips/cip1852/)
- Bech32 encoding/decoding per [CIP-0005](https://cips.cardano.org/cips/cip5/)

### Transaction Building
- Fluent API for transaction construction
- Fee calculation and coin selection
- Multi-asset transaction support
- Metadata handling

### Plutus Integration
- Smart contract interaction
- Script execution and validation
- ExUnit calculation for cost estimation
- Datum creation and validation

## 🧮 Installation

```bash
dotnet add package Chrysalis
```

## 📋 Code Examples

### Serialization & Deserialization

```csharp
// Deserialize a Cardano block from CBOR bytes
var block = CborSerializer.Deserialize<Block>(blockBytes);

// Access transaction data through extension methods
foreach (var tx in block.TransactionBodies())
{
    // Access inputs and outputs
    foreach (var input in tx.Inputs())
    {
        var txId = input.TransactionId();
        var index = input.Index();
    }
    
    foreach (var output in tx.Outputs())
    {
        var address = output.Address().ToBech32();
        var lovelace = output.Amount().Lovelace();
        var assets = output.Amount().MultiAsset();
    }
}

// Serialize a transaction back to CBOR
byte[] serializedTx = CborSerializer.Serialize(transaction);
```

### Address Handling

```csharp
// Create an address from Bech32 string
var address = Address.FromBech32("addr1qxck7...");

// Extract components
var paymentKeyHash = address.GetPaymentKeyHash();
var stakeKeyHash = address.GetStakeKeyHash();

// Create from components
var newAddress = Address.Create(
    AddressType.Base,
    NetworkType.Mainnet,
    paymentCredential,
    stakeCredential);

// Convert to Bech32 format
string bech32 = newAddress.ToBech32();
```

### Transaction Building

```csharp
// Create transaction builder
var builder = TransactionBuilder.Create(parameters)
    .SetNetworkId(NetworkType.Mainnet)
    .AddInputs(utxos) 
    .AddOutput(recipientAddress, 5_000_000) // 5 ADA
    .AddChangeOutput(changeAddress)
    .SetTtl(slot + 3600)
    .SetWitnesses(witnesses);

// Build transaction
var transaction = builder.Build();

// Sign transaction
var signedTx = transaction.Sign(privateKey);

// Serialize for submission
var serializedTx = CborSerializer.Serialize(signedTx);
```

### Node Communication

```csharp
// Create TCP connection to node
var bearer = new TcpBearer("127.0.0.1", 3001);

// Create node client
var nodeClient = new NodeClient(bearer);
await nodeClient.ConnectAsync();

// Use chain sync protocol
var chainSync = nodeClient.GetProtocol<ChainSync>();
var intersect = await chainSync.FindIntersectionAsync(points);

// Request chain tip
var tip = await chainSync.GetTipAsync();

// Submit transaction
var txSubmit = nodeClient.GetProtocol<LocalTxSubmit>();
await txSubmit.SubmitTxAsync(serializedTx);
```

### Plutus Script Execution

```csharp
// Create evaluator
var evaluator = new Evaluator();

// Evaluate transaction with Plutus scripts
var result = evaluator.EvaluateTx(transaction, datums, redeemers);

// Check results
foreach (var scriptResult in result.Results)
{
    Console.WriteLine($"Script passed: {scriptResult.Passed}");
    Console.WriteLine($"ExUnits: {scriptResult.ExUnits.Mem}/{scriptResult.ExUnits.Steps}");
}
```

## 📚 Module Structure

Chrysalis consists of several integrated modules:

- **Chrysalis.Cbor** - Core serialization and data models
- **Chrysalis.Network** - Node communication and protocol implementations
- **Chrysalis.Wallet** - Key and address management
- **Chrysalis.Tx** - Transaction building and submission
- **Chrysalis.Plutus** - Plutus script integration

## 🛠️ Advanced Usage

### Custom Data Types

```csharp
// Define a custom CBOR-serializable type
[CborSerializable(CborType.Map)]
public record MyDatum(
    [CborProperty(0)] CborBytes PolicyId,
    [CborProperty(1)] CborUlong Amount,
    [CborProperty(2)] Option<PlutusData> ExtraData
) : RawCbor;

// Use with Plutus smart contracts
var datumHash = DataHashUtil.Hash(CborSerializer.Serialize(myDatum));
```

### Multi-Asset Transactions

```csharp
// Create multi-asset output
var builder = TransactionBuilder.Create(parameters)
    .AddOutput(recipientAddress, new Value(
        lovelace: 2_000_000,
        multiAsset: new MultiAsset()
            .Add(policyId, assetName, 10)
    ))
    .AddChangeOutput(changeAddress);
```

### Script Execution

```csharp
// Add redeemer to transaction
builder.AddPlutusScript(
    scriptHash,
    RedeemerTag.Spend,
    plutusData,
    exUnits);
```

## ⚡ Performance

Chrysalis is not just feature-rich but also optimized for performance. Our benchmarks show that Chrysalis outperforms similar Rust-based libraries (including Pallas) in critical operations:

<div align="center">
  <img src="assets/chrysalis-benchmarks.png" alt="Chrysalis Performance Benchmarks" width="80%">
</div>

Key performance highlights:
- **Faster Block Deserialization** - Pure .NET implementation processes blocks more efficiently
- **Optimized Chain Sync** - Outperforms native Rust implementations in block retrieval
- **Efficient DB Operations** - Maintains performance edge even with database insertions
- **Memory-Efficient Processing** - Designed for handling large blockchain data volumes

This exceptional performance makes Chrysalis ideal for high-throughput applications like indexers and block explorers.

## 🔄 Interoperability

Chrysalis is designed to work seamlessly with:

- **Cardano Node** - Direct communication via mini-protocols
- **Block Explorers** - Process and index blockchain data
- **Wallets** - Build wallets for desktop, web or mobile
- **Smart Contracts** - Create and validate on-chain scripts

## 🤝 Contributing

We welcome contributions from the community! Here's how to get started:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Run tests (`dotnet test`)
5. Commit your changes (`git commit -m 'Add some amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

## 📜 License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## 🔗 Related Projects

- [Pallas](https://github.com/txpipe/pallas) - Rust-native building blocks for Cardano
- [Lucid](https://github.com/spacebudz/lucid) - JavaScript/TypeScript library for Cardano
- [PyCardano](https://github.com/cffls/pycardano) - Python library for Cardano

---

<div align="center">
  <p>Built with ❤️ by <a href="https://saib.dev">SAIB Inc</a></p>
</div>
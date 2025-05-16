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

<p align="center">
  A comprehensive .NET framework for Cardano blockchain development
</p>

## 📖 Overview

**Chrysalis** is a comprehensive, native .NET ecosystem for Cardano blockchain development. It provides .NET developers with a complete toolkit for interacting with the Cardano blockchain through:

- **CBOR Serialization** - Foundation for all Cardano data
- **Network Communications** - Connect directly to Cardano nodes
- **Wallet Management** - Create and manage addresses and keys
- **Transaction Building** - Construct and sign complex transactions
- **Smart Contract Integration** - Interact with and validate Plutus scripts

Whether you're building a wallet, explorer, DApp backend, or any Cardano-based application in .NET, Chrysalis provides the building blocks you need.

## 🚀 Key Features

- 🔄 **Full Cardano Support** - Compatible with the latest Cardano network
- 🚀 **Modern C# API** - Utilizing the latest features of C# and .NET
- 🧩 **Modular Architecture** - Use only the components you need
- 🔒 **Type Safety** - Strong typing for Cardano-specific data structures
- ⚡ **High Performance** - Outperforms equivalent libraries in other languages

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
- Template-based transaction creation for common patterns

### Plutus Integration
- Smart contract interaction
- Script execution and validation
- ExUnit calculation for cost estimation
- Datum creation and validation

## 🧩 Project Structure

Chrysalis consists of several specialized libraries that work together:

### Chrysalis.Cbor
A CBOR serialization library designed for Cardano data structures. Uses source generation instead of reflection for improved performance.

**Key Features:**
- Attribute-based serialization rules
- Source generation via `Chrysalis.Cbor.CodeGen`
- High-performance cbor encoding/decoding

### Chrysalis.Network
Implementation of Ouroboros mini-protocols following the Cardano network specification.

**Key Features:**
- Full implementation of Cardano network protocols
- Connection management with Cardano nodes
- Protocol message handling

### Chrysalis.Tx
Transaction building library with both low-level and high-level APIs.

**Key Features:**
- Low-level transaction builder for full flexibility
- Template transaction builder reduces boilerplate code
- Multiple supported providers (Ouroboros, Blockfrost, etc.)

### Chrysalis.Plutus
.NET port of the Pallas library, focusing on Phase-2 validation for smart contract transactions.

**Key Features:**
- Smart contract evaluation
- ExUnit calculation
- Script validation

### Chrysalis.Wallet
Wallet management library for Cardano.

**Key Features:**
- Mnemonic generation and recovery
- Key derivation
- Address generation and validation
- Cryptographic utilities

## 📥 Installation

```bash
# Install the main package
dotnet add package Chrysalis --version 0.7.3

# Or install individual components
dotnet add package Chrysalis.Cbor
dotnet add package Chrysalis.Network
dotnet add package Chrysalis.Tx
dotnet add package Chrysalis.Plutus
dotnet add package Chrysalis.Wallet
```

## 📋 Code Examples

### Defining a CBOR Serializable Type
```csharp
/*
cbor hex: d8799f581cc05cb5c5f43aac9d9e057286e094f60d09ae61e8962ad5c42196180c9f4040ff1a00989680ff
diagnostic:
121_0([_
    h'c05cb5c5f43aac9d9e057286e094f60d09ae61e8962ad5c42196180c',
    [_ h'', h''],
    10000000_2,
])
*/
[CborSerializable]
[CborConstr(0)]
public partial record MyTestCborType(
    [CborOrder(0)] byte[] address,
    [CborOrder(1)] AssetClass asset,
    [CborOrder(2)] ulong amount 
): CborBase;

[CborSerializable]
[CborList]
public partial record AssetClass(
    [CborOrder(0)] byte[] PolicyId,
    [CborOrder(1)] byte[] AssetName
) : CborBase


// main.cs
var data = "d8799f581cc05cb5c5f43aac9d9e057286e094f60d09ae61e8962ad5c42196180c9f4040ff1a00989680ff";
MyTestCborType myType = CborSerializer.Deserialize<MyTestCborType>(data);

// back to cbor
var myTypeCbor = CborSerializer.Serialize(myType);
```

### Working with the Wallet

```csharp
// Restore wallet from mnemonic
string words = "your mnemonic here";
Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);

// Derive account key
PrivateKey accountKey = mnemonic
    .GetRootKey()
    .Derive(PurposeType.Shelley, DerivationType.HARD)
    .Derive(CoinType.Ada, DerivationType.HARD)
    .Derive(0, DerivationType.HARD);

// Get payment key
PrivateKey paymentKey = accountKey
    .Derive(RoleType.ExternalChain)
    .Derive(0);

// Get staking key
PrivateKey stakingKey = accountKey
    .Derive(RoleType.Staking)
    .Derive(0);

// Generate public keys
PublicKey paymentPubKey = paymentKey.GetPublicKey();
PublicKey stakingPubKey = stakingKey.GetPublicKey();

// Create an address instance from the pub and priv key
Address address = new(NetworkType.Testnet, AddressType.BasePayment, paymentPubKey, stakingPubKey);

// Get the bech32 encoding of the address
string bech32Address = address.ToBech32();
```

### Accessing Block Data

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

### Connecting to a Cardano Node

```csharp
// Create a connection to a Cardano node
NodeClient client = await NodeClient.ConnectAsync("/ipc/node.socket");
await client.StartAsync(networkMagic: 2);

// Query Utxos
byte[] addr = Convert.FromHexString("00a7e1d2e57b1f9aa851b08c8934a315ffd97397fa997bb3851c626d3bb8d804d91fa134757d1a41b0b12762f8922fe4b4c6faa5ffec1bc9cf");
var utxos = await client.LocalStateQuery.GetUtxosByAddressAsync([addr]);
```

### Building a Simple Transaction with Low-level API

```csharp
var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");
var utxos = await provider.GetUtxosAsync(address.ToBech32());
var pparams = await provider.GetParametersAsync();
var txBuilder = TransactionBuilder.Create(pparams);

var output = new PostAlonzoTransactionOutput(
    new CborAddress(address.ToBytes()),
    new Lovelace(10000000UL),
    null,
    null
);

ResolvedInput? feeInput = null;
foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount().Lovelace()))
{
    if (utxo.Output.Amount().Lovelace() >= 5000000UL && utxo.Output.Amount() is Lovelace)
    {
        feeInput = utxo;
        break;
    }
}

if (feeInput is not null)
{
    utxos.Remove(feeInput);
    txBuilder.AddInput(feeInput.Outref);
}

var coinSelectionResult = CoinSelectionUtil.LargestFirstAlgorithm(utxos, [output.Amount]);

foreach (var consumed_input in coinSelectionResult.Inputs)
{
    txBuilder.AddInput(consumed_input.Outref);
}

ulong feeInputLovelace = feeInput?.Output.Amount()!.Lovelace() ?? 0;

Lovelace lovelaceChange = new(coinSelectionResult.LovelaceChange + feeInputLovelace);

Value changeValue = lovelaceChange;

if (coinSelectionResult.AssetsChange.Count > 0)
{
    changeValue = new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(coinSelectionResult.AssetsChange));
}

var changeOutput = new PostAlonzoTransactionOutput(
    new CborAddress(address.ToBytes()),
    changeValue,
    null,
    null
);

txBuilder
    .AddOutput(output)
    .AddOutput(changeOutput, true)
    .CalculateFee();

var unsignedTx = txBuilder.Build();
Transaction signedTx = unsignedTx.Sign(privateKey);
```

### Building a Simple Transaction using Templates
```csharp
var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");
string testSender = "addr_test1qpw9cvvdq8mjncs9e90trvpdvg7azrncafv0wtgvz0uf9vhgjp8dc6v79uxw0detul8vnywlv5dzyt32ayjyadvhtjaqyl2gur";
string testReceiver = "addr_test1qpw9cvadq8mjncs9e90trvpdvg7azrncafv0wtgvz0uf9vhgjp8dc6v79uxw0detul8vnywlv5dzyt32ayjyadvhtjaqyl2auv";

var transfer = TransactionTemplateBuilder<ulong>.Create(provider)
.AddStaticParty("test_sender", testSender, true) // isChange is set to true, this address will receive any change from this tx
.AddStaticParty("test_receiver", testReceiver)
.AddInput((options, amount) =>
{
    options.From = "test_sender";
})
.AddOutput((options, amount) =>
{
    options.To = "test_receiver";
    options.Amount = new Lovelace(amount);

})
.Build();

Transaction unlockUnsignedTx = await transfer(10000000UL);
Transaction unlockSignedTx = unlockUnsignedTx.Sign(privateKey);
```

### Building a Smart Contract Transaction using Templates
```csharp
var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");
string myAddress = "addr_test1qpw9cvvdq8mjncs9e90trvpdvg7azrncafv0wtgvz0uf9vhgjp8dc6v79uxw0detul8vnywlv5dzyt32ayjyadvhtjaqyl2gur";
string validatorAddress = "addr_test1wrf8enqnl26m0q5cfg73lxf4xxtu5x5phcrfjs0lcqp7uagh2hm3k";

string scriptRefTxHash = "c2609532a76c3f1d38a9e7192bb32946f9ca8ab47c635d99dffa3a3da7c9a218";
string lockUtxoTxHash = "0e2c71f1650c55be158b27ba8a03ff4f7c60ae6ba6706aba30fb5a918e91c25c";

var unlockLovelace = TransactionTemplateBuilder<UnlockParameters>.Create(provider)
    .AddStaticParty("rico", myAddress, true)
    .AddStaticParty("validator", validatorAddress)
    .AddInput((options, unlockParams) =>
    {
        options.From = "validator";
        options.UtxoRef = new TransactionInput(Convert.FromHexString(scriptRefTxHash), 0);
        options.IsReference = true;
    })
    .AddInput((options, unlockParams) =>
    {
        options.From = "validator";
        options.UtxoRef = new TransactionInput(Convert.FromHexString(lockUtxoTxHash), 0);
        options.Redeemer = unlockParams.Redeemer;
    })
    .AddOutput((options, unlockParams) =>
    {
        options.To = "rico";
        options.Amount = unlockParams.Amount;
    })
    .Build();


UnlockParameters unlockParams = new(
    new TransactionInput(Convert.FromHexString(lockUtxoTxHash), 0),
    new TransactionInput(Convert.FromHexString(scriptRefTxHash), 0),
    new Lovelace(20000000),
    new RedeemerMap(new Dictionary<RedeemerKey, RedeemerValue>() { { new RedeemerKey(0, 0), new RedeemerValue(new PlutusConstr([]){ConstrIndex = 121}, new ExUnits(1400000, 100000000)) } }
));

Transaction unlockUnsignedTx = await unlockLovelace(unlockParams);
Transaction unlockSignedTx = unlockUnsignedTx.Sign(privateKey);
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

## ⚡ Performance

Chrysalis is not just feature-rich but also optimized for performance. Our benchmarks show that Chrysalis outperforms similar Rust-based libraries (including Pallas) in critical operations:

<div align="center">
  <img src="assets/chrysalis_bechmark_with_db.png" alt="Chrysalis Performance Benchmarks (with DB)" width="80%">
</div>

<div align="center">
  <img src="assets/chrysalis_bechmark_no_db.png" alt="Chrysalis Performance Benchmarks (no DB)" width="80%">
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

## 📖 Documentation

For more detailed information, please refer to the following resources:

- [Chrysalis.CBOR Documentation](./CBOR.md)
- [Chrysalis.Tx Documentation](./TX.md)

## 🤝 Contributing

Contributions are welcome! Here's how you can help:

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
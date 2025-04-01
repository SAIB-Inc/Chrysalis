
<div align="center">    
  <h1 style="font-size: 3em;">Chrysalis 🦋</h1>
</div>

<p align="center">
  <a href="https://github.com/yourusername/chrysalis/releases"><img src="https://img.shields.io/badge/status-beta-orange" alt="Status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License"></a>
  <a href="https://github.com/SAIB-Inc/chrysalis/issues"><img src="https://img.shields.io/github/issues/SAIB-Inc/chrysalis" alt="Issues"></a>
</p>

<p align="center">
  A comprehensive .NET framework for Cardano blockchain development
</p>

## 📖 Overview

Chrysalis is a suite of .NET libraries designed to simplify Cardano blockchain development. From CBOR serialization to transaction building, Chrysalis provides everything developers need to build applications on Cardano.

## ✨ Features

- 🚀 **Modern C# API** - Utilizing the latest features of C# and .NET Core
- 🔄 **Full Cardano Support** - Compatible with the latest Cardano network 
- 🧩 **Modular Architecture** - Use only the components you need
- ⚡ **High Performance** - Optimized for speed and efficiency
- 🔒 **Type Safety** - Strong typing for Cardano-specific data structures
- 🛠️ **Comprehensive Tools** - Everything needed for dApp development

## 📚 Table of Contents

- [Installation](#-installation)
- [Project Structure](#-project-structure)
- [Usage Examples](#-usage-examples)
- [Documentation](#-documentation)
- [Contributing](#-contributing)
- [License](#-license)
- [Contact](#-contact)

## 📥 Installation

Each Chrysalis component can be installed separately as needed:

```bash
# Install the CBOR serialization and source generation libraries
dotnet add package Chrysalis.Cbor
dotnet add package Chrysalis.Cbor.Codegen

# Install the Network library for Ouroboros mini-protocols
dotnet add package Chrysalis.Network

# Install the Transaction building library
dotnet add package Chrysalis.Tx

# Install the Plutus smart contract evaluation library
dotnet add package Chrysalis.Plutus

# Install the Wallet library
dotnet add package Chrysalis.Wallet
```

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
- Template transaction builder makes building transaction so much simpler by reducing boilerplate codes
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

## 🚀 Usage Examples

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
using Chrysalis.Wallet.Words;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;

// Restore wallet from mnemonic
string words = "address address address address address address address address address address address address";
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

### Connecting to a Cardano Node

```csharp
// Create a connection to a Cardano node
NodeClient client = await NodeClient.ConnectAsync("/ipc/node.socket");
client.Start();

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

## 📖 Documentation

For more detailed information, please refer to the following resources:

- [Chrysalis.CBOR Documentation](./CBOR.md)


## 🤝 Contributing

Contributions are welcome! Here's how you can help:

1. Fork the repository
2. Create a feature branch: `git checkout -b feat/amazing-feature`
3. Commit your changes: `git commit -m 'feat: add amazing feature'`
4. Push to the branch: `git push origin feature/amazing-feature`
5. Open a Pull Request

Please make sure to update tests as appropriate.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 📞 Contact

[Your Contact Information]

---

<p align="center">
  Made with ❤️ for the Cardano community
</p>

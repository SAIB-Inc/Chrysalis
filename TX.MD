# Chrysalis.Tx

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

A powerful and flexible library for building Cardano transactions, offering both low-level control and high-level abstractions, built for .NET.

## 📋 Overview

Chrysalis.Tx is a comprehensive .NET library that simplifies the process of creating and managing Cardano blockchain transactions. Whether you're building a simple ADA transfer application or implementing complex smart contract interactions, Chrysalis.Tx provides the tools you need with both detailed control and convenient abstractions.

## 🔑 Key Features
- **Dual-layer approach**: Choose between low-level control and high-level abstractions
- **Template system**: Create reusable transaction patterns with parameterization
- **Fluent API**: Intuitive builder pattern for clear and maintainable code

## 🚀 Getting Started

### Installation

```bash
dotnet add package Chrysalis --version 0.7.2
```

## Raw Transaction Builder

The TransactionBuilder provides granular control over every aspect of your Cardano transactions.

### Basic Usage

```csharp
using Chrysalis.Tx.Builders;

var txBuilder = new TransactionBuilder()
    .AddInput(new TransactionInput(txId, index))
    .AddOutput(new TransactionOutput(address, amount))
    .SetFee(170000)
    .SetTtl(12345678)
    .AddVKeyWitness(vkeyWitness);

var transaction = txBuilder.Build();
```

### Capabilities:

- Precise control over inputs, outputs, collateral, and reference inputs
- Manual configuration of fees, TTL, and validity intervals
- Full support for certificates, withdrawals, and minting operations
- Complete witness management (VKey witnesses, native scripts, Plutus scripts)

## 📝 Template Transaction Builder

The TransactionTemplateBuilder<T> offers a higher-level abstraction for creating reusable transaction templates.

### Basic Usage

```csharp
using Chrysalis.Tx.Builders;

var transfer = TransactionTemplateBuilder<ulong>.Create(provider)
    .AddStaticParty("ana", anaAddress, isChange: true);
    .AddStaticParty("bob", bobAddres)
    .AddInput((options, param) =>
    {
        options.From = "ana"
    })
    .AddOutput((options, param) => 
    {
        option.To = "bob"
        options.Amount = new Lovelace(amount)
    })
    .Build()
    
var transaction = await transfer(10000000);
```

### Advantages:

- ✅ Reusable templates with parameterization
- ✅ Automatic fee calculation and change handling
- ✅ Smart UTXO selection
- ✅ Simplified party management

## 🧩 Advanced Scenarios

### Example with Redeemer Builder

Below is a simplified example demonstrating how to use the TransactionTemplateBuilder with a redeemer builder 

```csharp
// Define parameters for unlocking assets from a script
public class UnlockParams
{
    public string UserAddress { get; set; }   
    public string ScriptAddress { get; set; }       
    public TransactionInput ScriptRefUtxo { get; set; } 
    public TransactionInput LockedUtxo { get; set; }  
}

// Create a redeemer builder for the script
RedeemerDataBuilder<UnlockParams, PlutusData> spendRedeemerBuilder = (mapping, parameters) =>
{
    // TransactionTemplateBuilder handles redeemer tags (like RedeemerTag.Spend) and indices automatically
    // You only need to focus on crafting the correct data structure for your script
    
    // This is a simple placeholder - in a real scenario, you'd create the appropriate
    // data structure required by your specific Plutus script
    return new PlutusConstr([])
    {
        ConstrIndex = 121 // Your script's required redeemer value
    };
};

// The mapping parameter provides access to input-output associations:
// mapping: Dictionary<int, Dictionary<string, int>>
// This lets you reference specific transaction components by index
// For example, to access the first input's index: mapping[0]["input"]

// Build a template for unlocking assets
var unlock = TransactionTemplateBuilder<UnlockParams>.Create(provider)
    .AddStaticParty("user", userAddress, isChange: true)
    .AddStaticParty("validator", validatorAddress)
    .AddInput((options, param) =>
    {
        options.From = "validator";
        options.UtxoRef = param.ScriptRefUtxo;
        options.IsReference = true;
    })  
    .AddInput((options, param) => 
    {
        options.From = "validator";
        options.UtxoRef = param.LockedUtxo;
        options.SetRedeemerBuilder(spendRedeemerBuilder);
    })
    .Build();

// Execute the template with specific parameters
Transaction tx = await unlock(new UnlockParams(
    "addr1q9...",
    "addr1w8...",
    new TransactionInput("hash456...", 0),
    new TransactionInput("hash789...", 1)
));
```

### Key advantages:

- **Automatic tag handling**: TransactionTemplateBuilder automatically assigns the correct redeemer tags (like RedeemerTag.Spend, RedeemerTag.Mint, etc.) based on context.
- **Index management**: Redeemer indices are automatically calculated, eliminating a common source of errors.
- **Transaction context access**: The mapping parameter exposes a dictionary structure that provides access to all input and output indices:
```csharp
// Structure: Dictionary<string, (ulong InputIndex, Dictionary<string, ulong> OutputIndexes)>
// Where the key is the party name, and the value contains:
// - InputIndex: The index of the input associated with this party
// - OutputIndexes: A dictionary mapping output names to their indices

// Example: referencing specific transaction components
mapping["inputId1"].InputIndex;
mapping["inputId2"].OutputIndexes["outputName"];
```
- **Script optimization**: This is particularly valuable for smart contracts that need to reference specific UTXOs or outputs, enabling precise control over redeemer data based on the actual transaction structure.

**Example scenario**: A smart contract that requires a redeemer to reference both its input position and a specific output:

indices:
```csharp
RedeemerDataBuilder<ContractParams, PlutusData> complexRedeemerBuilder = (mapping, parameters) =>
{
    // Get the index of the input associated with a specific party
    ulong scriptInputIndex = mapping["validator"].InputIndex;
    
    // Get the index of a specific output
    ulong userOutputIndex = mapping["user"].OutputIndexes["change"];
    
    // Create a redeemer that references these specific transaction components
    return new PlutusConstr([
        new PlutusInteger((long)scriptInputIndex),
        new PlutusInteger((long)userOutputIndex)
    ])
    {
        ConstrIndex = 0
    };
};


# Chrysalis.Tx Documentation

## Overview

The `Chrysalis.Tx` module provides comprehensive transaction building and submission capabilities for the Cardano blockchain. It leverages the CBOR serialization and network components to create, sign, and submit transactions across different Cardano eras.

## Key Features

- **Fluent builder API** - Intuitive transaction construction
- **Template-based transactions** - Reusable patterns for common transaction types
- **Fee calculation** - Automatic fee estimation based on transaction size
- **Coin selection** - Smart UTXO management strategies
- **Multi-asset support** - Native token creation and transfer
- **Smart contract integration** - Plutus script execution and validation

## Transaction Building

### Fluent API

The fluent transaction builder provides a step-by-step approach to transaction construction:

```csharp
var transaction = TransactionBuilder.Create(protocolParams)
    .SetNetworkId(0)  // testnet
    .AddInput(input)
    .AddOutput(output)
    .AddCollateral(collateralInput)
    .AddMint(policyScript, assetName, 1000)
    .SetTtl(slotNumber + 7200)
    .SetFee(1000000UL)
    .Build();
```

### Template-Based Building

For common transaction patterns, templates provide a reusable approach:

```csharp
// Define a reusable template
var template = TransactionTemplateBuilder<TransferParams>.Create(provider)
    .AddStaticParty("sender", senderAddress, true)
    .AddStaticParty("receiver", receiverAddress)
    .AddInput(options => options.From = "sender")
    .AddOutput((options, params) => {
        options.To = "receiver";
        options.Amount = params.Amount;
    })
    .Build();

// Use the template with different parameters
var tx1 = await template(new TransferParams { Amount = 50_000_000 });
var tx2 = await template(new TransferParams { Amount = 25_000_000 });
```

## Fee Calculation

```csharp
// Calculate minimum fee based on transaction size
ulong fee = FeeUtil.CalculateFee(
    transaction, 
    protocolParams.MinFeeA, 
    protocolParams.MinFeeB
);

// Auto-set fee
var tx = builder.SetAutoFee().Build();
```

## Working with UTXOs

```csharp
// Coin selection for inputs
var selectedUtxos = CoinSelectionUtil.SelectUtxos(
    availableUtxos,
    outputs.Sum(o => o.GetAmount().GetLovelace()),
    MinUtxoValue
);

// Add selected UTXOs as inputs
foreach (var utxo in selectedUtxos)
{
    builder.AddInput(utxo);
}

// Add change output if needed
if (changeAmount > 0)
{
    builder.AddOutput(
        changeAddress,
        Value.Lovelace(changeAmount)
    );
}
```

## Smart Contract Transactions

### Spending from Script Addresses

```csharp
// Spending from a script address
builder.AddInput(
    scriptInput,
    new Redeemer(RedeemerTag.Spend, 0, plutusData, ExUnits.Initial),
    datum
);

// Reference script
builder.AddInput(
    scriptRefInput,
    isReference: true
);
```

### Plutus Script Evaluation

```csharp
// Build transaction with estimated ExUnits
var transaction = builder.Build();

// Evaluate script execution costs
var results = evaluator.EvaluateTx(
    CborSerializer.Serialize(transaction),
    CborSerializer.Serialize(resolvedInputs)
);

// Update transaction with actual costs
foreach (var result in results)
{
    var redeemer = transaction.GetWitnessSet()
        .GetRedeemers().FirstOrDefault(r => 
            r.Tag == result.RedeemerTag && 
            r.Index == result.Index);
            
    if (redeemer != null)
    {
        redeemer.ExUnits = result.ExUnits;
    }
}
```

## Era-Specific Features

### Alonzo Features

- Smart contract support with Plutus scripts
- Collateral inputs for failed script execution
- Script data integrity hashes

### Babbage/Vasil Features

- Reference inputs for read-only UTXO access
- Reference scripts for reusing on-chain scripts
- Inline datums for efficiency

### Conway Features

- Governance actions for on-chain voting
- DRep registrations and delegations
- Constitutional committee operations

## Transaction Providers

The library includes providers for sourcing blockchain data:

```csharp
// Use Blockfrost API
var blockfrost = new Blockfrost("apiKey", NetworkType.Testnet);
var utxos = await blockfrost.GetUtxosAsync(address);
var params = await blockfrost.GetProtocolParametersAsync();

// Use direct node connection
var ouroboros = new Ouroboros(nodeClient);
var tip = await ouroboros.GetChainTipAsync();
```

## Best Practices

1. **Error Handling** - Always include proper try/catch blocks
2. **Fee Buffer** - Add a small buffer to calculated fees
3. **TTL Setting** - Set reasonable time-to-live values (1-2 hours)
4. **UTXO Management** - Implement proper coin selection
5. **Script Testing** - Test script transactions thoroughly before mainnet
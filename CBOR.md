# Chrysalis.Cbor

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

A high-performance CBOR (Concise Binary Object Representation) serialization library specifically designed for Cardano blockchain applications, built for .NET.

## 📋 Overview

Chrysalis.Cbor provides a strongly-typed, attribute-based approach to serialize and deserialize complex CBOR data in the Cardano ecosystem. It uses C# source generators to produce highly efficient serialization code at compile time, eliminating runtime reflection overhead.

The library features:

- 🚀 High-performance serialization/deserialization
- 🔍 First-class support for Cardano data structures
- ⚙️ Attribute-based customization
- 🧩 Support for complex CBOR data types
- 🔄 Handling of indefinite-length structures

## 🚀 Getting Started

### Installation

```bash
dotnet add package Chrysalis --version 0.7.2
```

### Basic Usage

```csharp
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;

// Serialize an object to CBOR
byte[] cborData = CborSerializer.Serialize(myCardanoObject);

// Deserialize from CBOR 
MyCardanoType result = CborSerializer.Deserialize<MyCardanoType>(cborData);
```

## 📝 Key Concepts

Chrysalis.Cbor uses an attribute-based model to define serialization behavior.

### Base Requirements

- All serializable types must be decorated with `[CborSerializable]`
- All serializable types must inherit from `CborBase`
- All serializable types must be declared as `partial` records

```csharp
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

[CborSerializable]
public partial record MyCardanoType(int Value) : CborBase;
```

### CBOR Structure Types

### List Type

```csharp
[CborSerializable]
[CborList]
public partial record TransactionInput(
    [CborOrder(0)] byte[] TransactionId,  // Order is required for lists
    [CborOrder(1)] ulong Index
) : CborBase;
```

### Map Type

```csharp
[CborSerializable]
[CborMap]
public partial record BabbageTransactionBody(
    [CborProperty(0)] CborMaybeIndefList<TransactionInput> Inputs,
    [CborProperty(1)] CborMaybeIndefList<TransactionOutput> Outputs,
    [CborProperty(2)] ulong Fee
    // ...other properties
) : CborBase, ICborPreserveRaw;
```

### Union Type

```csharp
[CborSerializable]
[CborUnion]
public abstract partial record TransactionOutput : CborBase { }

[CborSerializable]
[CborList]
public partial record AlonzoTransactionOutput(
    [CborOrder(0)] Address Address,
    [CborOrder(1)] Value Amount,
    [CborOrder(2)] byte[]? DatumHash
) : TransactionOutput, ICborPreserveRaw;

[CborSerializable]
[CborMap]
public partial record PostAlonzoTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Value Amount,
    [CborProperty(2)] DatumOption? Datum,
    [CborProperty(3)] CborEncodedValue? ScriptRef
) : TransactionOutput, ICborPreserveRaw;
```

### Types with Cbor Tag

```csharp
[CborSerializable]
[CborTag(30)]  // Apply tag 30 (rational number in CBOR)
[CborList]
public partial record CborRationalNumber(
    [CborOrder(0)] ulong Numerator,
    [CborOrder(1)] ulong Denominator
) : CborBase;
```

### Constructor Type

```csharp
[CborSerializable]
[CborConstr(0)]  // Constructor with tag 121 (0 + 121)
public partial record Some<T>([CborOrder(0)] T Value) : Option<T>;

[CborSerializable]
[CborConstr]  // This means it can be any tag upon deserialization and defaults to 0 when serialized unless the ConstrIndex is changed.
public partial record Some<T>([CborOrder(0)] T Value) : Option<T>;
```

## 🧩 Attribute Reference

### Type Attributes

| Attribute | Description |
|-----------|-------------|
| `[CborSerializable]` | Required on all serializable types |
| `[CborList]` | Reads/writes as CBOR array |
| `[CborMap]` | Reads/writes  as CBOR map |
| `[CborUnion]` | Abstract type that can be any of its inherited types |
| `[CborConstr(int)] or [CborConstr]` | Reads/writes as constructor with index that starts at 121  |
| `[CborTag(int)]` | Reads/writes CBOR tag before the data |

### Property Attributes

| Attribute | Description |
|-----------|-------------|
| `[CborOrder(int)]` | Specifies property order in lists (required) |
| `[CborProperty(key)]` | Specifies property key in maps |
| `[CborSize(int)]` | Divides byte array into chunks of specified size |
| `[CborNullable]` | Encodes null values as CBOR null |
| `[CborIndefinite]` | Encodes as indefinite-length structure |

## 🛠️ Special Types

### Container Type

Simple container for a single value:

```csharp
[CborSerializable]
public partial record ContainerType(int Value) : CborBase;
```

### Option Type

```csharp
// Usage example
[CborSerializable]
[CborMap]
public partial record MyType(
    [CborProperty(0)] Option<string> OptionalText
) : CborBase;

// Creating instances
var withValue = new MyType(new Some<string>("Hello"));
var withoutValue = new MyType(new None<string>());
```
### CborMaybeIndefList

```csharp
// Definite length list
var definiteList = new CborDefList<int>(new List<int> { 1, 2, 3 });

// Indefinite length list
var indefiniteList = new CborIndefList<int>(new List<int> { 1, 2, 3 });

// Definite length list with tag 258
var taggedDefList = new CborDefListWithTag<int>(new List<int> { 1, 2, 3 });

// Indefinite length list with tag 258
var taggedIndefList = new CborIndefListWithTag<int>(new List<int> { 1, 2, 3 });
```

## 🛡️ Validation

Chrysalis.Cbor provides validation capabilities through the `ICborValidator<T>` interface:

```csharp
public interface ICborValidator<T>
{
    bool Validate(T input);
}
```

Implementing this interface allows you to define custom validation logic that will be:

- Executed before serialization to ensure only valid objects are serialized
- Executed after deserialization to verify the deserialized data meets your requirements

Example implementation:

```csharp
// A simple container type that holds an integer value
[CborSerializable]
public partial record PositiveIntContainer(int Value) : CborBase;

// Validator that ensures the integer is within a valid range
public class PositiveIntValidator : ICborValidator<PositiveIntContainer>
{
    public bool Validate(PositiveIntContainer container)
    {
        // Only allow positive values between 1 and 100
        return container.Value > 0 && container.Value <= 100;
    }
}
```

This validation mechanism helps enforce business rules and data constraints throughout the serialization process.

## 🧬 Cardano Support

Chrysalis.Cbor includes first-class support for Cardano blockchain data structures:

- Blocks and block headers
- Transactions and transaction bodies
- Transaction inputs and outputs
- Certificates and witnesses
- Metadata
- And much more...

Example of a Cardano transaction:

```csharp
[CborSerializable]
[CborList]
public partial record ShelleyTransaction(
   [CborOrder(0)] TransactionBody TransactionBody,
   [CborOrder(1)] TransactionWitnessSet TransactionWitnessSet,
   [CborOrder(2)][CborNullable] Metadata? TransactionMetadata
) : Transaction, ICborPreserveRaw;
```

## 🔍 Performance Considerations

- Uses source generators to create serialization code at compile time
- Avoids reflection for better performance
- Preserves original CBOR data when possible with `ICborPreserveRaw`

## 📚 Advanced Usage

### Preserving Raw CBOR Data

Implementing `ICborPreserveRaw` preserves the original CBOR bytes in the `Raw` property:

```csharp
[CborSerializable]
[CborList]
public partial record BlockHeader(
    [CborOrder(0)] BlockHeaderBody HeaderBody,
    [CborOrder(1)] byte[] BodySignature
) : CborBase, ICborPreserveRaw;
```

When you deserialize a type that implements ICborPreserveRaw, the original CBOR byte data will be stored in the `Raw` property. This is useful when you need to:

- Preserve the exact binary representation for later use
- Pass the raw data to other systems without altering the original data
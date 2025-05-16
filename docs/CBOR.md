# Chrysalis.Cbor Documentation

## Overview

The `Chrysalis.Cbor` module provides high-performance CBOR serialization and deserialization for Cardano data structures. It is built on top of the .NET System.Formats.Cbor library with optimizations and extensions for Cardano-specific CBOR encodings.

## Key Features

- **Attribute-based serialization** - Simple annotation of your models for automatic serialization
- **Source generation** - Compile-time code generation for maximum performance
- **Extension method pattern** - Clean API for accessing nested data structures
- **Comprehensive Cardano types** - Full implementation of Cardano on-chain data models

## Serialization Attributes

Chrysalis.Cbor uses attributes to control how objects are serialized:

| Attribute | Description |
|-----------|-------------|
| `[CborSerializable]` | Marks a class or record for CBOR serialization |
| `[CborProperty]` | Marks a property to be serialized |
| `[CborOrder]` | Specifies the order of fields in a definite-length structure |
| `[CborList]` | Serializes a type as a CBOR list |
| `[CborMap]` | Serializes a type as a CBOR map |
| `[CborConstr]` | Marks a Plutus constructor with its tag |
| `[CborIndefinite]` | Marks a container as indefinite-length |

## Basic Usage

```csharp
// Define a CBOR-serializable type
[CborSerializable]
[CborList]
public partial record Person(
    [CborOrder(0)] string Name,
    [CborOrder(1)] int Age
) : CborBase;

// Serialize to CBOR bytes
Person person = new("John", 30);
byte[] bytes = CborSerializer.Serialize(person);

// Deserialize from CBOR bytes
Person deserialized = CborSerializer.Deserialize<Person>(bytes);
```

## Extension Method Pattern

Chrysalis.Cbor makes extensive use of extension methods to provide a clean API for accessing nested data structures:

```csharp
// Without extensions
var recipients = transaction.TransactionBody.Outputs
    .Where(o => o.Address.Equals(myAddress))
    .Select(o => o.Amount);

// With extensions
var recipients = transaction.GetOutputs()
    .Where(o => o.GetAddress().Equals(myAddress))
    .Select(o => o.GetAmount());
```

## Source Generation

For best performance, Chrysalis uses source generation to create optimized serialization code at compile time:

```csharp
// The [CborSerializable] attribute triggers source generation
[CborSerializable]
public partial record MyType(...) : CborBase;

// Source generator creates:
// - Serialize() method
// - Deserialize() method
// - Other helper methods
```

## Cardano Era Support

| Era | Serialization Support |
|-----|----------------------|
| Byron | Planned for future releases |
| Shelley | Fully supported |
| Allegra | Fully supported |
| Mary | Fully supported |
| Alonzo | Fully supported |
| Babbage | Fully supported |
| Conway | Fully supported |

## Performance Considerations

- Use `CborBase.Raw` to access the underlying CBOR bytes for a type
- Utilize `ICborPreserveRaw` for preserving original byte representation
- For large objects, consider using stream-based serialization

## Advanced Usage

### Custom Converters

For types that need special handling:

```csharp
public class MyCustomConverter : ICborConverter<MyCustomType>
{
    public void Write(CborWriter writer, MyCustomType value)
    {
        // Custom writing logic
    }

    public MyCustomType Read(ref CborReader reader)
    {
        // Custom reading logic
    }
}
```

### Working with Raw CBOR

```csharp
// Create a raw CBOR value
var raw = CborEncodedValue.Create(bytes);

// Access raw CBOR bytes after serialization
var transaction = CborSerializer.Deserialize<Transaction>(bytes);
byte[] originalBytes = transaction.Raw!;
```

## Best Practices

1. Always extend `CborBase` for serializable types
2. Use `CborOrder` attributes to ensure consistent field ordering
3. Prefer records over classes for immutability
4. Use extension methods for cleaner access patterns
5. Leverage source generation for performance
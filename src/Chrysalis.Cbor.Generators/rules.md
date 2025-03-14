# CBOR Serialization Rules for Cardano

This document defines the rules for CBOR serialization in the Cardano C# library, detailing how C# types map to CBOR encoding and how serialization can be customized using attributes.

## 1. Type Mappings

### 1.1 Base Types

| C# Type          | CBOR Type          | Major Type | Notes                                     |
|------------------|--------------------|-----------|--------------------------------------------|
| `bool`           | Simple value       | 7         | `true` or `false`                          |
| `byte`, `sbyte`  | Integer            | 0/1       | Unsigned/signed 8-bit integer              |
| `short`, `ushort`| Integer            | 0/1       | Unsigned/signed 16-bit integer             |
| `int`, `uint`    | Integer            | 0/1       | Unsigned/signed 32-bit integer             |
| `long`, `ulong`  | Integer            | 0/1       | Unsigned/signed 64-bit integer             |
| `BigInteger`     | Integer/Tag+Bytes  | 0/1/6     | Arbitrary size integer, uses tags 2 and 3  |
| `float`          | Single-precision   | 7         | IEEE 754 Single-precision float            |
| `double`         | Double-precision   | 7         | IEEE 754 Double-precision float            |
| `decimal`        | Tag + Array        | 6 + 4     | Tagged decimal as array of components      |
| `string`         | Text string        | 3         | UTF-8 encoded                              |
| `byte[]`         | Byte string        | 2         | Raw bytes                                  |
| `ReadOnlyMemory<byte>` | Byte string  | 2         | Raw bytes, more efficient for large data   |
| `DateTime`       | Tag + Integer/String | 6 + 0/3 | Standard date/time format                  |
| `Guid`           | Tag + Byte string  | 6 + 2     | 16 bytes, tag 37                           |
| `null`           | Simple value       | 7         | null value                                 |

### 1.2 Cardano-Specific Types

| C# Type          | CBOR Type          | Notes                                     |
|------------------|--------------------|--------------------------------------------|
| `Hash28`         | Byte string        | 28-byte hash (addresses, key hashes)      |
| `Hash32`         | Byte string        | 32-byte hash (txs, blocks)                |
| `Coin`           | Integer            | Amount in lovelace                        |
| `UnitInterval`   | Tag + Array        | Tag 30, fraction between 0 and 1          |
| `RationalNumber` | Tag + Array        | Tag 30, arbitrary fraction                |
| `NetworkId`      | Integer            | 0=Testnet, 1=Mainnet                      |

### 1.3 Collection Types

| C# Type                   | CBOR Type    | Major Type | Notes                           |
|---------------------------|--------------|-----------|----------------------------------|
| `T[]`                     | Array        | 4         | Fixed-size array                 |
| `List<T>`                 | Array        | 4         | Dynamic array                    |
| `IEnumerable<T>`          | Array        | 4         | Sequence                         |
| `Dictionary<TKey,TValue>` | Map          | 5         | Key-value pairs                  |
| `IDictionary<TKey,TValue>`| Map          | 5         | Key-value pairs                  |
| `HashSet<T>`              | Array/Tag    | 4/6       | Set with unique elements         |
| `ISet<T>`                 | Array/Tag    | 4/6       | Set with unique elements         |

## 2. Serialization Attributes

### 2.1 Type-Level Attributes

| Attribute                 | Description                                              | Example                           |
|---------------------------|----------------------------------------------------------|-----------------------------------|
| `// [CborSerializable]`      | Marks a type for CBOR serialization                      | `// [CborSerializable] public class Transaction {...}` |
| `[CborTag(ulong)]`        | Applies a CBOR tag to the serialized output              | `[CborTag(121)] public record PlutusData {...}` |
| `[CborMap]`               | Serializes type as a CBOR map                            | `[CborMap] public record Metadata {...}` |
| `[CborArray]`             | Serializes type as a CBOR array                          | `[CborArray] public record Coordinates {...}` |
| `[CborConstr(int)]`       | Serializes type as a constructor with tag                | `[CborConstr(0)] public record Choice {...}` |
| `[CborUnion]`             | Marks an abstract type as a union                        | `[CborUnion] public abstract record Value {...}` |
| `[CborOrder(int)]`        | Order of serde for Constr, Map, or Array/List            | `[CborConstr(0)] public record Choice { [CborOrder(0)]FirstProperty }` |             

### 2.2 Property-Level Attributes

| Attribute                 | Description                                              | Example                           |
|---------------------------|----------------------------------------------------------|-----------------------------------|
| `[CborProperty(int?)]`    | Maps property to index/key in CBOR                       | `[CborProperty(0)] public string Id { get; set; }` |
| `[CborIgnore]`            | Excludes property from serialization                     | `[CborIgnore] public string LocalNote { get; set; }` |
| `[CborRequired]`          | Marks property as required for deserialization           | `[CborRequired] public Hash32 TxId { get; set; }` |
| `[CborIndefinite]`        | Serializes collection as indefinite-length               | `[CborIndefinite] public List<int> Counts { get; set; }` |
| `[CborCustom(Type)]`      | Uses custom converter for property                       | `[CborCustom(typeof(MyConverter))] public Data D { get; set; }` |
| `[CborSize(int)]`         | Specifies fixed size for byte arrays                     | `[CborSize(32)] public byte[] Hash { get; set; }` |

### 2.3 Validation Attributes

| Attribute                   | Description                                              | Example                           |
|-----------------------------|----------------------------------------------------------|-----------------------------------|
| `[CborValidateExact(value)]`| Ensures primitive value matches exactly                  | `[CborValidateExact(1)] public int Version { get; set; }` |
| `[CborValidateRange(min, max)]`| Ensures numeric value falls within specified range    | `[CborValidateRange(0, 100)] public int Percentage { get; set; }` |
| `[CborValidateEnum]`        | Ensures value is valid for the enum type                 | `[CborValidateEnum] public NetworkId Network { get; set; }` |
| `[CborValidatePattern(regex)]`| Validates string against regex pattern                 | `[CborValidatePattern("^[a-f0-9]{64}$")] public string TxHash { get; set; }` |
| `[CborValidate(Type)]`      | Uses custom validator for complex types                  | `[CborValidate(typeof(AddressValidator))] public Address Addr { get; set; }` |

## 3. Union Types

CBOR for Cardano heavily uses union types. We support several patterns:

### 3.1 Abstract Record Pattern (Recommended)

Uses C# record hierarchies with discriminated tag values:

```csharp
[CborUnion]
public abstract record PlutusData
{
    [CborConstr(0)]
    public record Constr(int Tag, List<PlutusData> Fields) : PlutusData;
    
    [CborConstr(1)]
    public record Map(Dictionary<PlutusData, PlutusData> Entries) : PlutusData;
    
    [CborConstr(2)]
    public record List(List<PlutusData> Items) : PlutusData;
    
    [CborConstr(3)]
    public record Integer(BigInteger Value) : PlutusData;
    
    [CborConstr(4)]
    public record Bytes(ReadOnlyMemory<byte> Data) : PlutusData;
}
```

### 3.2 OneOf Pattern (For Simple Unions)

For unions of different types without a common base:

```csharp
// [CborSerializable]
public class MetadataField
{
    [CborProperty(0)]
    [CborOneOf(typeof(string), typeof(int), typeof(byte[]))]
    public OneOf<string, int, byte[]> Value { get; set; }
}
```

### 3.3 Nested Unions

Unions can be nested within other unions:

```csharp
[CborUnion]
public abstract record Value
{
    [CborConstr(0)]
    public record Coin(ulong Amount) : Value;
    
    [CborConstr(1)]
    public record MultiAsset(ulong Amount, Dictionary<PolicyId, Assets>) : Value;
    
    [CborUnion]
    public abstract record Assets : Value
    {
        [CborConstr(0)]
        public record Named(string Name, ulong Amount) : Assets;
        
        [CborConstr(1)]
        public record Raw(ReadOnlyMemory<byte> Name, ulong Amount) : Assets;
    }
}
```

## 4. Special CBOR Structures

### 4.1 Constructor Tags (Plutus Data)

Plutus data uses special tags (121-127, 1280-1400) for constructors:

```csharp
[CborConstr(0)]  // Will be encoded with tag 121
public record ConstrPlutus0(List<PlutusData> Fields) : PlutusData;

[CborConstr(1)]  // Will be encoded with tag 122
public record ConstrPlutus1(List<PlutusData> Fields) : PlutusData;

[CborConstr(102, 453)]  // Will be encoded with tag 102 and constructor 453
public record ConstrPlutusOther(List<PlutusData> Fields) : PlutusData;
```

### 4.2 Maps with Different Key/Value Types

For maps with varying key/value types:

```csharp
[CborMap]
public class HeterogeneousMap
{
    [CborMapEntries]
    public Dictionary<OneOf<string, int>, OneOf<string, bool, float>> Entries { get; set; }
}
```

### 4.3 Indefinite Length Collections

For collections that should use indefinite length encoding:

```csharp
// [CborSerializable]
public class Transaction
{
    [CborProperty(0)]
    [CborIndefinite]
    public List<TransactionInput> Inputs { get; set; }
}
```

### 4.4 Fixed Size Types

For byte arrays with fixed size requirements:

```csharp
// [CborSerializable]
public class BlockHeader
{
    [CborProperty(0)]
    [CborSize(32)]  // Must be exactly 32 bytes
    public byte[] PrevBlockHash { get; set; }
}
```

### 4.5 Value Validation

For properties that require specific values or ranges:

```csharp
// [CborSerializable]
public class ProtocolParameters
{
    // Must be exactly 1
    [CborProperty(0)]
    [CborValidateExact(1)]
    public int Version { get; set; }
    
    // Must be between 0 and 1000
    [CborProperty(1)]
    [CborValidateRange(0, 1000)]
    public int MaxBlockSize { get; set; }
    
    // Must be a valid NetworkId value
    [CborProperty(2)]
    [CborValidateEnum]
    public NetworkId Network { get; set; }
    
    // Must match the regex pattern
    [CborProperty(3)]
    [CborValidatePattern("^[a-f0-9]{64}$")]
    public string GenesisHash { get; set; }
    
    // Uses custom validator
    [CborProperty(4)]
    [CborValidate(typeof(AddressValidator))]
    public Address Treasury { get; set; }
}

// Custom validator example
public class AddressValidator : ICborValidator<Address>
{
    public bool Validate(Address value, out string errorMessage)
    {
        // Custom validation logic here
        if (!value.IsValid())
        {
            errorMessage = "Invalid address format";
            return false;
        }
        errorMessage = null;
        return true;
    }
}
```

## 5. Source Generation Rules

The source generator will:

1. Find all types marked with `// [CborSerializable]`, `[CborMap]`, `[CborArray]`, `[CborUnion]` or `[CborConstr]`
2. Generate extension methods for serialization/deserialization
3. Follow these rules:
   - Respect all attribute configurations
   - Generate type-safe code without boxing/unboxing where possible
   - Use efficient byte handling for large byte arrays
   - Generate readable and debuggable code
   - Apply validation rules during deserialization

```csharp
// Generated extension methods:
public static byte[] ToCbor(this Transaction tx);
public static Transaction FromCbor(byte[] data);
```

## 6. Examples

### 6.1 Transaction with Multiple Output Types

```csharp
// [CborSerializable]
public class Transaction
{
    [CborProperty(0)]
    public List<TransactionInput> Inputs { get; set; }
    
    [CborProperty(1)]
    public List<TransactionOutput> Outputs { get; set; }
    
    [CborProperty(2)]
    [CborValidateRange(0, ulong.MaxValue)]
    public ulong Fee { get; set; }
}

[CborUnion]
public abstract record TransactionOutput
{
    [CborConstr(0)]
    public record Legacy(string Address, ulong Amount) : TransactionOutput;
    
    [CborConstr(1)]
    public record PostAlonzo(string Address, ulong Amount, Hash32? DatumHash) : TransactionOutput;
}
```

### 6.2 PlutusData

```csharp
[CborUnion]
public abstract record PlutusData
{
    [CborConstr(0)]  // Will use tag 121
    public record Constr(int Tag, List<PlutusData> Fields) : PlutusData;
    
    [CborMap]
    public record Map(Dictionary<PlutusData, PlutusData> Entries) : PlutusData;
    
    [CborArray]
    public record List(List<PlutusData> Items) : PlutusData;
    
    public record Integer(BigInteger Value) : PlutusData;
    
    public record Bytes(byte[] Data) : PlutusData;
}
```

### 6.3 MultiAsset Value with Validation

```csharp
[CborUnion]
public abstract record Value
{
    public record Coin([CborValidateRange(0, ulong.MaxValue)] ulong Amount) : Value;
    
    public record MultiAsset(
        [CborValidateRange(0, ulong.MaxValue)] ulong Amount, 
        Dictionary<PolicyId, Dictionary<AssetName, ulong>> Assets) : Value;
}
```

### 6.4 Protocol Parameters with Validation

```csharp
// [CborSerializable]
public class ProtocolParameters
{
    [CborProperty(0)]
    [CborValidateExact(1)]
    public int Version { get; set; }
    
    [CborProperty(1)]
    [CborValidateRange(1, 65536)]
    public int MaxBlockSize { get; set; }
    
    [CborProperty(2)]
    [CborValidateRange(1, 100)]
    public int MaxTxSize { get; set; }
    
    [CborProperty(3)]
    [CborValidateRange(0.0, 1.0)]
    public double PoolInfluence { get; set; }
    
    [CborProperty(4)]
    [CborValidateEnum]
    public NetworkId NetworkId { get; set; }
}
```

6.5 This can handle arbitrary validation outside the compile-time constant constraints

```csharp
// [CborSerializable]
[CborConstr(0)]
public record ExactAddress(
  byte[] PaymentKeyHash,
  byte[] StakeKeyHash
)
{
    public bool Validate()
    {
       bool isCorrectPkh = Convert.FromHexString(PaymentKeyHash) == "someTestPkh";
       bool isCorrectSkh = Convert.FromHexString(StakeKeyHash) == "someTestSkh";

       return isCorrectPkh && isCorrectSkh;
    }
}

```


# Sample Source Gen

## Union Type
Old Design
```csharp
[CborConverter(typeof(UnionConverter))]
public abstract partial record TransactionOutput : CborBase;


[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record AlonzoTransactionOutput(
    [CborIndex(0)] Address Address,
    [CborIndex(1)] Value Amount,
    [CborIndex(2)] CborBytes? DatumHash
) : TransactionOutput;


[CborConverter(typeof(CustomMapConverter))]
[CborOptions(IsDefinite = true)]
public partial record PostAlonzoTransactionOutput(
    [CborIndex(0)] Address Address,
    [CborIndex(1)] Value Amount,
    [CborIndex(2)] DatumOption? Datum,
    [CborIndex(3)] CborEncodedValue? ScriptRef
) : TransactionOutput;
```

New Design
```csharp
// [CborSerializable]
[CborUnion]
public abstract partial record TransactionOutput
{
    [CborList]
    public partial record AlonzoTransactionOutput(
        [CborOrder(0)]
        Address Address,

        [CborOrder(1)]
        Value Amount,

        [CborOrder(2)]
        byte[]? DatumHash
    ) : TransactionOutput;

    [CborMap]
    public partial record PostAlonzoTransactionOutput(
        [CborOrder(0)]
        Address Address,

        [CborOrder(1)]
        Value Amount,

        [CborOrder(2)]
        byte[]? DatumOption? Datum,

        [CborOrder(3)]
        CborEncodedValue? ScriptRef
    ) : TransactionOutput;
}
```

Sample Code Generated From This Type
```csharp

public abstract partial record TransactionOutput
{

  static List<Type> types = new()
  [
    AlonzoTransactionOutput,
    PostAlonzoTransactionOutput
  ];

  public static void Write(CborReader writer, TransactionOutput data)
  {
        CborSerializer.Serialize(data);
  }

   public static TransactionOutput Read(CborReader reader)
   {
        var encodedValue = reader.ReadEncodedValue();

        foreach(Type type in types)
        {
          CborReader subReader = new CborReader(encodedValue);
          if (TryRead(reader, type, out var? result))
          {
            return result;
          }
        }

        throw new Exception($"Failed to deserialize type {TransactionOutput}");
   }

   public static bool TryRead(CborReader reader, Type type, out TransactionOutput? result)
   {
        try 
        {
          result = CborSerializer.Deserialize(reader, type);
          return true;
        } 
        catch
        {
          return false;
        }
   }
}
```

## Custom List

Old Design
```csharp
[CborConverter(typeof(CustomListConverter))]
public partial record InlineDatumOption(
    [CborIndex(0)] CborInt Option,
    [CborIndex(1)] CborEncodedValue Data
) : CborBase;
```

New Design
```csharp
[CborList]
public partial record InlineDatumOption(
    [CborIndex(0)]
    int Option,

    [CborIndex(1)] 
    CborEncodedValue Data
);
```
Sample Code Generated From This Type

```csharp
  public partial record InlineDatumOption
  {
    public static void Write(CborWriter writer, InlineDatumOption data)
    {
        writer.WriteStartArray();

        writer.WriteInt32(data.Option);
        CborEncodedValue.Write(writer, data.Data);

        writer.WriteEndArray();
    }

    public static InlineDatumOption Read(CborReader reader)
    {
      reader.ReadStartArray();

      var option = read.ReadInt32;
      var data = CborSerialize.Read(reader, CborEncodedValue);

      reader.ReadEndArray();

      return new InlineDatumOption(
        option,
        data
      );
    }
  }
```

## Custom Map

Old Design
```csharp
[CborConverter(typeof(CustomMapConverter))]
[CborOptions(IsDefinite = true)]
public partial record PostAlonzoTransactionOutput(
    [CborIndex(0)] Address Address,
    [CborIndex(1)] Value Amount,
    [CborIndex(2)] DatumOption? Datum,
    [CborIndex(3)] CborEncodedValue? ScriptRef
) : TransactionOutput;
```

New Design
```csharp
[CborMap]
public partial record PostAlonzoTransactionOutput(
    [CborProperty("0")] Address Address,
    [CborProperty("1")] Value Amount,
    [CborProperty("2")] DatumOption? Datum,
    [CborProperty("3")] CborEncodedValue? ScriptRef
);
```

Sample Code Generated From This Type

```csharp
  public partial record PostAlonzoTransactionOutput
  {

    readonly Dictionary<string, (string, Type)> _mapping = new()
    {
      { "0" : ("Address", Address) },
      { "1" : ("Amount", Value) },
      { "2" : ("Datum", DatumOption) },
      { "3" : ("ScriptRef", CborEncodedValue) }
    }

    public static void Write(CborWriter writer, PostAlonzoTransactionOutput data)
    {
        writer.StartMap()

        writer.WriteByteString("0");
        Address.Write(data.Address);

        writer.WriteByteString("1");
        Value.Write(data.Amount);

        writer.WriteByteString("2");
        DatumOption.Write(data.Datum);

        writer.WriteByteString("3");
        CborEncodedValue.Write(writer, data.Data);

        writer.EndMap()
    }

    public static PostAlonzoTransactionOutput Read(CborReader reader)
    {
       Address? address = null;
       Value? amount = null;
       DatumOption? datum = null;
       CborEncodedValue? scriptRef = null;

       reader.ReadStartMap();
       while(reader.PeekState() != CborState.ReadEndMap)
       {
            string key = reader.ReadTextString();
              
            switch (_mapping[key].Item1)
            {
                case "Address":
                    address = Address.Read(reader);
                    break;
                case "Amount":
                    amount = Value.Read(reader);
                    break;
                case "Datum":
                    datum = DatumOption.Read(reader);
                    break;
                case "ScriptRef":
                    scriptRef = CborEncodedValue.Read(reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
       }

       reader.ReadEndMap();

       return new PostAlonzoTransactionOutput(
          address,
          amount,
          datum,
          scriptRef
       );
    }
  }
```
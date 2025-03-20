## Overview of Rules

### Type Attributes
[CborSerializable] - this attribute is used to tell the source generator that this type needs a cbor serializer
[CborList] [CborMap] [CborConstr] [CborUnion] [CborTag] - a helper attribute to assist source gen in determining the code to generate

### Property Attributes
[CborOrder(int)] - used for properties within a [CborList] or [CborConstr] to preserve the ordering of properties
[CborProperty(int/string)] - used for properties within a [CborMap] to specify the `key` to serialize/deserialize
[CborSize(int)] - used to specify that the type has definite size. `byte[]` support will be initially supported
[CborNullable] - used to specify if a property can be serialized into `null` 
[CborIndefinite] - by default, arrays and maps will be serialized into definite length, this tells the source generator to serialize into indefinite length

### Special Types
1. `CborEncodedValue` - a special type for serializing data with encoding tag
2. Nested record for `Union` types
3. Open generic types, for example `CborDefiniteList<T>`

### Primitive Types
`int`
`long`
`ulong`
`double`
`decimal`
`string`
`byte[]` - with special handling since [CborSize(int)] can be used for this type

### Collection Types 
#### in order to not complicate things, we will only support these 2 types for now
`IEnumerable<T>`  
`Dictionary<TKey, TValue>`

### Special Rules
`ICborValidator<T>` - is an interface with one function `Validate()` to tell generator that a type must be validated after deserialization or before serialization
`ICborRaw` - is an interface with no function, this just helps the source gen to know if a validate function must be called or not
`CborBase<T>` - is base abstract type that contains the `Raw` property and `Name` which is a helper for union types. All custom types must inherit from CborBase<T> in order for serialization to work properly

## Development Checklist

### Base Code
 [] Create Chrysalis.Cbor.CodeGen dotnet project

### Metadata Gathering
 [] Create a file `CborSerializerCodeGen.Metadata.cs` which will contain the metadata types
 [] Define a type to hold the metadata for the type `CborSerializableTypeMetadata` which includes `typeName`, `namespace`, `attributes`, `interfaces`
 [] Create a file `CborSerializerCodeGen.Parser.cs` which will contain the logic on how to extract the metadata
 [] Create `CborSerializerCodeGen.cs` class which will be the starting point of the generator 
 [] Emit a file per type that contains the metadata for debugging

### Code Generation
 [] Create a file `CborSerializerCodeGen.Emitter.cs` that contains the logic for generating code, depending on the rules, this will contain the emitters for primitive types and collection types
    [CborList] [CborMap] [CborConstr] [CborUnion] will have their own reusable emitters. [CborTag] [CborNullable] [CborSize] [CborIndefinite] emitter helpers will be on the generic emitter file
 [] Create a folder `Emitters` that has `ICborEmitter` interface that has 2 methods `GenerateSerializer` and `GenerateDeserializer`
 [] Create primitive type emitter and make sure they work with test types
 [] Create collection type (`IEnumerable`, `Dictionary<TKey, TValue>`) emitters with support for closed generics and make sure it works with test types
 [] Create [CborList] `ICborEmitter` and test with test types
 [] Create [CborConstr] `ICborEmitter` and test with test types
 [] Create [CborMap] `ICborEmitter` and test with test types
 [] Create [CborUnion] `ICborEmitter` and test with test types
 [] Open Generic handling for example `CborDefiniteList<T>`

 ### Testing and debugging
 #### Basic
 [] Primitive 
 [] List
 [] Map
 [] Nullable
 [] Indefinite
 [] Bounded Bytes
 [] Cbor Tag

 #### Complex Types
 [] Container
 [] Constr
 [] Custom List
 [] Custom Map
 [] Union Type
 [] Generic Type

 #### Cardano Types
 [] Block
    [] Header
    [] TransactionBody
    [] WitnessSet
    [] AuxDataSet
    [] InvalidTransactions

[] Alonzo Block
[] Babbage Block
[] Conway Block
